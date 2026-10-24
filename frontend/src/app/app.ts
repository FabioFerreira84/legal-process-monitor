import { Component, signal, computed, inject, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProcessoService } from './services/processo.service';
import {
  Processo,
  DocumentoProcessual,
  PerfilMagistrado,
  AdvogadoCarteira,
  ProcessoResumo,
  RiscoForense,
  TermoFatalCalculado,
  ProcessoWatchlistItem
} from './models/processo.model';
import {
  calcularTermoFatalForense,
  extrairDataCivilSegura,
  formatarDataBr
} from './utils/calculadora-prazo';

export interface CnjDecodificado {
  valido: boolean | null;
  motivoErro?: string;
  sequencial?: string;
  dv?: string;
  ano?: string;
  ramoJustica?: string;
  tribunal?: string;
  origem?: string;
  formatado?: string;
}

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  private readonly processoService = inject(ProcessoService);

  // Modo de Busca: CNJ ou OAB
  modoBusca = signal<'cnj' | 'oab'>('cnj');

  // Estados principais
  numeroProcessoInput = signal('2101760-84.2025.8.26.0000');
  termoOabInput = signal('238470SP');
  carregando = signal(false);
  buscandoOab = signal(false);
  erro = signal<string | null>(null);
  processoAtual = signal<Processo | null>(null);
  advogadoCarteira = signal<AdvogadoCarteira | null>(null);
  abaAtiva = signal<'capa' | 'documentos' | 'timeline' | 'inteligencia' | 'dados'>('capa');
  filtroTextoTimeline = signal('');
  copiado = signal(false);
  whatsappCopiado = signal(false);

  // Controles interativos da Calculadora Forense de Termo Fatal (CPC/2015 + DJEN)
  prazoBaseCustomizado = signal<number | null>(null);
  prazoEmDobroAtivo = signal<boolean>(false);
  ehDisponibilizacaoDjenAtivo = signal<boolean>(true);

  // Status da Nuvem e Ping
  statusNuvem = signal<'online' | 'aquecendo' | 'offline'>('aquecendo');
  tempoLatenciaNuvem = signal<number | null>(null);

  // Spotlight Search (Ctrl + K)
  spotlightAberto = signal(false);
  spotlightTermo = signal('2101760-84.2025.8.26.0000');
  historicoConsultas = signal<string[]>([]);

  // Radar de Consulta (Progresso por Etapas)
  radarAtivo = signal(false);
  radarEtapa = signal(1);
  radarEtapaTexto = signal('Iniciando validação...');
  private radarTimer: any = null;

  // Visualizador Forense In-App de Peças / Acórdãos
  visualizadorAberto = signal(false);
  documentoSelecionado = signal<DocumentoProcessual | null>(null);

  // Ficha Institucional do Juízo / Relatoria & Notas do Escritório
  magistradoModalAberto = signal(false);
  magistradoSelecionado = signal<PerfilMagistrado | null>(null);
  notaEscritorioTexto = signal('');
  notaSalvaFeedback = signal(false);

  // Watchlist e Monitoramento de Processos
  watchlist = signal<ProcessoWatchlistItem[]>([]);
  watchlistPainelAberto = signal(false);

  // Parser em tempo real do CNJ no Spotlight
  cnjDecodificado = computed<CnjDecodificado>(() => {
    const termo = this.spotlightTermo().trim();
    return this.decodificarCnj(termo);
  });

  // Movimentações filtradas dinamicamente
  movimentacoesFiltradas = computed(() => {
    const proc = this.processoAtual();
    if (!proc) return [];
    const filtro = this.filtroTextoTimeline().toLowerCase().trim();
    if (!filtro) return proc.movimentacoes;

    return proc.movimentacoes.filter(
      (m) =>
        m.titulo.toLowerCase().includes(filtro) ||
        m.descricao?.toLowerCase().includes(filtro) ||
        (m.codigoNacional && m.codigoNacional.toString().includes(filtro))
    );
  });

  // 1. MOTOR HEURÍSTICO DE RISCO FORENSE
  alertaRiscoForense = computed<RiscoForense | null>(() => {
    const proc = this.processoAtual();
    if (!proc || !proc.movimentacoes || proc.movimentacoes.length === 0) return null;

    for (let i = 0; i < Math.min(8, proc.movimentacoes.length); i++) {
      const mov = proc.movimentacoes[i];
      const tit = (mov.titulo || '').toLowerCase();
      const desc = (mov.descricao || '').toLowerCase();
      const texto = `${tit} ${desc}`;

      if (
        texto.includes('sisbajud') ||
        texto.includes('renajud') ||
        texto.includes('penhora') ||
        texto.includes('bloqueio de ativos') ||
        texto.includes('mandado de prisão')
      ) {
        return {
          nivel: 'critico',
          titulo: 'Alerta de Constrição Patrimonial (SISBAJUD / Penhora)',
          descricao: `Detectada determinação constritiva no andamento: "${mov.titulo}".`,
          termoDetectado: mov.titulo,
          recomendacaoTatica:
            'Verificar cabimento de impugnação, substituição por seguro-garantia/fiança bancária (Art. 835, § 2º CPC) ou arguição de impenhorabilidade (Art. 833 CPC).',
          dataEvento: mov.dataHora
        };
      }

      if (
        texto.includes('in albis') ||
        texto.includes('decurso de prazo') ||
        texto.includes('revelia') ||
        texto.includes('trânsito em julgado') ||
        texto.includes('baixa definitiva')
      ) {
        return {
          nivel: 'critico',
          titulo: 'Alerta de Preclusão / Encerramento de Fase Processual',
          descricao: `Certificado decurso de prazo ou encerramento nos autos: "${mov.titulo}".`,
          termoDetectado: mov.titulo,
          recomendacaoTatica:
            'Conferir tempestividade e certidão de disponibilização no DJEN (Res. CNJ 455/2022) ou eventual hipótese do Art. 223 do CPC.',
          dataEvento: mov.dataHora
        };
      }

      if (
        texto.includes('efeito suspensivo') ||
        texto.includes('concedida a liminar') ||
        texto.includes('provimento em parte') ||
        texto.includes('provimento ao recurso')
      ) {
        return {
          nivel: 'favoravel',
          titulo: 'Pronunciamento Judicial de Provimento / Tutela Deferida',
          descricao: `Decisão registrada nos autos: "${mov.titulo}".`,
          termoDetectado: mov.titulo,
          recomendacaoTatica:
            'Verificar comunicação ao juízo de origem e eventuais prazos recursais da parte adversa.',
          dataEvento: mov.dataHora
        };
      }

      if (
        texto.includes('djen') ||
        texto.includes('publicação') ||
        texto.includes('disponibilizado') ||
        texto.includes('intimada') ||
        texto.includes('vista') ||
        texto.includes('contrarrazões')
      ) {
        const dtBr = formatarDataBr(extrairDataCivilSegura(mov.dataHora));
        return {
          nivel: 'atencao',
          titulo: 'Comunicação Processual / Disponibilização no DJEN',
          descricao: `Marco de intimação registrado em ${dtBr}: "${mov.titulo}".`,
          termoDetectado: mov.titulo,
          recomendacaoTatica:
            'Conferir contagem em dias úteis na Calculadora Forense abaixo (Art. 219 e Art. 224, §§ 2º e 3º do CPC).',
          dataEvento: mov.dataHora
        };
      }
    }

    return {
      nivel: 'informativo',
      titulo: 'Tramitação Processual Regular',
      descricao:
        'Nenhuma ordem de bloqueio ou decurso in albis imediato identificado nos últimos andamentos.',
      termoDetectado: proc.movimentacoes[0]?.titulo || 'Andamento regular',
      recomendacaoTatica: 'Acompanhar próximas disponibilizações via DJEN/CNJ.',
      dataEvento: proc.movimentacoes[0]?.dataHora || new Date().toISOString()
    };
  });

  // Extrai o marco de publicação mais recente dos autos
  marcoPublicacaoDetectado = computed(() => {
    const proc = this.processoAtual();
    if (!proc || !proc.movimentacoes || proc.movimentacoes.length === 0) return null;

    for (const mov of proc.movimentacoes) {
      const tit = (mov.titulo || '').toLowerCase();
      const desc = (mov.descricao || '').toLowerCase();
      if (
        tit.includes('djen') ||
        tit.includes('publicação') ||
        desc.includes('publicado') ||
        tit.includes('diário') ||
        desc.includes('disponibilizado') ||
        tit.includes('vista')
      ) {
        let diasSugeridos = 15;
        let tipoAto = 'Recurso / Manifestação Geral';
        if (tit.includes('embargos') || desc.includes('embargos')) {
          diasSugeridos = 5;
          tipoAto = 'Embargos de Declaração';
        } else if (tit.includes('informações') || desc.includes('10 dias')) {
          diasSugeridos = 10;
          tipoAto = 'Prestação de Informações / Manifestação';
        } else if (
          tit.includes('recurso especial') ||
          tit.includes('aresp') ||
          tit.includes('apelação') ||
          tit.includes('agravo') ||
          tit.includes('vista') ||
          desc.includes('contrarrazões')
        ) {
          diasSugeridos = 15;
          tipoAto = 'Recurso / Contrarrazões';
        }
        return { dataHora: mov.dataHora, diasSugeridos, tipoAto, tituloMovimento: mov.titulo };
      }
    }

    return {
      dataHora: proc.movimentacoes[0].dataHora,
      diasSugeridos: 15,
      tipoAto: 'Manifestação sobre Último Andamento',
      tituloMovimento: proc.movimentacoes[0].titulo
    };
  });

  // 2. CALCULADORA DINÂMICA DE TERMO FATAL (CPC/2015 + LEI 14.759/2023 + DJEN RES. CNJ 455/2022)
  termoFatalCalculado = computed<TermoFatalCalculado | null>(() => {
    const proc = this.processoAtual();
    const marco = this.marcoPublicacaoDetectado();
    if (!proc || !marco) return null;

    const diasBase = this.prazoBaseCustomizado() ?? marco.diasSugeridos;
    return calcularTermoFatalForense({
      dataMarco: marco.dataHora,
      diasPrazoBase: diasBase,
      ehDisponibilizacaoDjen: this.ehDisponibilizacaoDjenAtivo(),
      prazoEmDobro: this.prazoEmDobroAtivo(),
      siglaTribunal: proc.tribunal || 'TJSP',
      tipoAto:
        diasBase === 5
          ? 'Embargos de Declaração (5d)'
          : diasBase === 10
            ? 'Manifestação / Informações (10d)'
            : marco.tipoAto
    });
  });

  // Simulações comparativas reais (5d, 15d e 30d em dobro) para o mesmo marco processual
  simulacoesPrazo = computed(() => {
    const proc = this.processoAtual();
    const marco = this.marcoPublicacaoDetectado();
    if (!proc || !marco) return null;

    const trib = proc.tribunal || 'TJSP';
    const isDjen = this.ehDisponibilizacaoDjenAtivo();

    return {
      embargos5d: calcularTermoFatalForense({
        dataMarco: marco.dataHora,
        diasPrazoBase: 5,
        ehDisponibilizacaoDjen: isDjen,
        prazoEmDobro: false,
        siglaTribunal: trib,
        tipoAto: 'Embargos de Declaração'
      }),
      recurso15d: calcularTermoFatalForense({
        dataMarco: marco.dataHora,
        diasPrazoBase: 15,
        ehDisponibilizacaoDjen: isDjen,
        prazoEmDobro: false,
        siglaTribunal: trib,
        tipoAto: 'Recurso / Contrarrazões'
      }),
      dobro30d: calcularTermoFatalForense({
        dataMarco: marco.dataHora,
        diasPrazoBase: 15,
        ehDisponibilizacaoDjen: isDjen,
        prazoEmDobro: true,
        siglaTribunal: trib,
        tipoAto: 'Fazenda / Defensoria / MP (Dobro)'
      })
    };
  });

  selecionarPrazoBase(dias: number) {
    this.prazoBaseCustomizado.set(dias);
  }

  alternarPrazoEmDobro() {
    this.prazoEmDobroAtivo.set(!this.prazoEmDobroAtivo());
  }

  alternarModoDjen() {
    this.ehDisponibilizacaoDjenAtivo.set(!this.ehDisponibilizacaoDjenAtivo());
  }

  // 3. JURIMETRIA EMPÍRICA DOS AUTOS (100% CALCULADA SOBRE OS MOVIMENTOS E DOCUMENTOS REAIS)
  jurimetriaDinamica = computed(() => {
    const proc = this.processoAtual();
    if (!proc) return null;

    const movs = proc.movimentacoes || [];
    const docs = proc.documentosPublicos || [];
    const totalMovs = movs.length;

    const movsAsc = [...movs].sort(
      (a, b) =>
        extrairDataCivilSegura(a.dataHora).getTime() - extrairDataCivilSegura(b.dataHora).getTime()
    );

    const dataInicial: Date | null = proc.dataDistribuicao
      ? extrairDataCivilSegura(proc.dataDistribuicao)
      : movsAsc.length > 0
        ? extrairDataCivilSegura(movsAsc[0].dataHora)
        : null;

    let dataDecisao: Date | null = null;
    let tituloDecisao: string | null = null;
    let dataLiminar: Date | null = null;

    // Contadores reais de classificação de atos nos autos
    let qtdPublicacoes = 0;
    let qtdDecisoes = 0;
    let qtdPeticoesRecursos = 0;
    let qtdCartorioOutros = 0;

    for (const m of movsAsc) {
      const tit = (m.titulo || '').toLowerCase();
      const desc = (m.descricao || '').toLowerCase();
      const d = extrairDataCivilSegura(m.dataHora);

      if (
        !dataLiminar &&
        (tit.includes('outras decisões') ||
          tit.includes('liminar') ||
          tit.includes('tutela') ||
          desc.includes('efeito suspensivo') ||
          desc.includes('liminar'))
      ) {
        dataLiminar = d;
      }

      if (
        !dataDecisao &&
        (tit.includes('julgamento') ||
          tit.includes('acórdão') ||
          tit.includes('sentença') ||
          tit.includes('ausência das condições') ||
          desc.includes('deram provimento') ||
          desc.includes('sentença proferida'))
      ) {
        dataDecisao = d;
        tituloDecisao = m.titulo;
      }

      if (
        tit.includes('djen') ||
        tit.includes('publicação') ||
        tit.includes('expedição') ||
        tit.includes('intimação') ||
        tit.includes('vista') ||
        desc.includes('disponibilizado')
      ) {
        qtdPublicacoes++;
      } else if (
        tit.includes('julgamento') ||
        tit.includes('decis') ||
        tit.includes('despacho') ||
        tit.includes('sentença') ||
        tit.includes('acórdão') ||
        tit.includes('conclusão')
      ) {
        qtdDecisoes++;
      } else if (
        tit.includes('petição') ||
        tit.includes('recurso') ||
        tit.includes('agravo') ||
        tit.includes('embargos') ||
        tit.includes('pauta')
      ) {
        qtdPeticoesRecursos++;
      } else {
        qtdCartorioOutros++;
      }
    }

    const diasAteDecisao =
      dataInicial && dataDecisao
        ? Math.max(
            1,
            Math.round((dataDecisao.getTime() - dataInicial.getTime()) / (1000 * 3600 * 24))
          )
        : null;

    const diasAteLiminar =
      dataInicial && dataLiminar
        ? Math.max(
            0,
            Math.round((dataLiminar.getTime() - dataInicial.getTime()) / (1000 * 3600 * 24))
          )
        : null;

    let ritmoMedioDias = 0;
    let diasTramitacaoTotal = 0;
    let maiorHiatoDias = 0;
    let maiorHiatoPeriodo = 'Sem hiato relevante';

    if (movsAsc.length > 1) {
      const primeira = extrairDataCivilSegura(movsAsc[0].dataHora).getTime();
      const ultima = extrairDataCivilSegura(movsAsc[movsAsc.length - 1].dataHora).getTime();
      diasTramitacaoTotal = Math.max(1, Math.round((ultima - primeira) / (1000 * 3600 * 24)));
      ritmoMedioDias = +(diasTramitacaoTotal / (movsAsc.length - 1)).toFixed(1);

      for (let i = 1; i < movsAsc.length; i++) {
        const dAnt = extrairDataCivilSegura(movsAsc[i - 1].dataHora);
        const dAtu = extrairDataCivilSegura(movsAsc[i].dataHora);
        const diff = Math.round((dAtu.getTime() - dAnt.getTime()) / (1000 * 3600 * 24));
        if (diff > maiorHiatoDias) {
          maiorHiatoDias = diff;
          maiorHiatoPeriodo = `${formatarDataBr(dAnt)} a ${formatarDataBr(dAtu)}`;
        }
      }
    }

    const divisor = Math.max(1, totalMovs);
    const pPub = Math.round((qtdPublicacoes / divisor) * 100);
    const pDec = Math.round((qtdDecisoes / divisor) * 100);
    const pPet = Math.round((qtdPeticoesRecursos / divisor) * 100);
    const pCart = Math.max(0, 100 - (pPub + pDec + pPet));

    return {
      totalMovimentacoes: totalMovs,
      totalDocumentosDjen: docs.length,
      diasTramitacaoTotal,
      diasAteDecisao,
      tituloDecisao: tituloDecisao || 'Julgamento / Decisão',
      diasAteLiminar,
      ritmoMedioDias,
      maiorHiatoDias,
      maiorHiatoPeriodo,
      amostra: `Amostra Real: n = ${totalMovs} movimentações e ${docs.length} publicações DJEN nestes autos`,
      distribuicao: [
        {
          label: `Publicações DJEN & Intimações (${qtdPublicacoes})`,
          percent: pPub,
          color: 'parcial',
          highlight: pPub >= pDec
        },
        {
          label: `Decisões, Conclusões & Julgamentos (${qtdDecisoes})`,
          percent: pDec,
          color: 'provido',
          highlight: pDec > pPub
        },
        {
          label: `Petições, Recursos & Pauta (${qtdPeticoesRecursos})`,
          percent: pPet,
          color: 'improvido',
          highlight: false
        },
        {
          label: `Atos Ordinatórios & Remessas (${qtdCartorioOutros})`,
          percent: pCart,
          color: 'nao-conhecido',
          highlight: false
        }
      ].filter((item) => item.percent > 0)
    };
  });

  // Síntese dinâmica para o cliente (baseada nos dados reais do processo aberto)
  resumoExecutivoCliente = computed(() => {
    const proc = this.processoAtual();
    if (!proc) return null;

    const ultMov = proc.movimentacoes?.[0];
    const ultDoc = proc.documentosPublicos?.[0];
    const dataUltMov = ultMov ? formatarDataBr(extrairDataCivilSegura(ultMov.dataHora)) : 'recente';

    const oQueDecidiu = ultDoc
      ? `Publicação oficial registrada (${ultDoc.titulo}): "${ultDoc.ementa || ultDoc.conteudoTexto}".`
      : ultMov
        ? `Último andamento registrado em ${dataUltMov}: "${ultMov.titulo} — ${ultMov.descricao || ''}".`
        : `Processo em acompanhamento perante o ${proc.tribunal}.`;

    const impactoPratico = proc.status
      ? `Status processual atual: ${proc.status} perante ${proc.orgaoJulgador || proc.tribunal}.`
      : `Autos ativos na unidade ${proc.orgaoJulgador || proc.tribunal}.`;

    const prazo = this.termoFatalCalculado();
    const proximoPasso =
      prazo && !prazo.precluso
        ? `Acompanhamento de prazo em curso (${prazo.tipoAto}) com vencimento calculado para ${prazo.dataTermoFatal} (${prazo.diasUteisRestantes} dias úteis restantes).`
        : `Monitoramento contínuo de novas disponibilizações no Diário de Justiça Eletrônico Nacional (DJEN/CNJ).`;

    return { oQueDecidiu, impactoPratico, proximoPasso };
  });

  // Links oficiais de pesquisa de jurisprudência nos tribunais
  urlPesquisaJurisprudenciaOficial = computed(() => {
    const proc = this.processoAtual();
    if (!proc) return 'https://scon.stj.jus.br/SCON/';
    if (proc.tribunal === 'TJSP') return 'https://esaj.tjsp.jus.br/cjsg/consultaCompleta.do';
    if (proc.tribunal === 'TRF3') return 'https://web.trf3.jus.br/base-textual';
    return 'https://scon.stj.jus.br/SCON/';
  });

  // 4. WATCHLIST STATUS
  estaNaWatchlist = computed(() => {
    const proc = this.processoAtual();
    if (!proc) return false;
    return this.watchlist().some((w) => w.numeroCnjLimpo === proc.numeroCnjLimpo);
  });

  // Atalhos Globais de Teclado
  @HostListener('window:keydown', ['$event'])
  handleKeyboardEvent(event: KeyboardEvent) {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.abrirSpotlight();
    } else if (event.key === 'Escape') {
      if (this.magistradoModalAberto()) {
        this.fecharDossieMagistrado();
      } else if (this.visualizadorAberto()) {
        this.fecharDocumento();
      } else if (this.spotlightAberto()) {
        this.fecharSpotlight();
      } else if (this.watchlistPainelAberto()) {
        this.watchlistPainelAberto.set(false);
      }
    }
  }

  ngOnInit() {
    this.carregarHistorico();
    this.carregarWatchlist();
    this.executarSilentPing();
    this.buscarProcesso(false, true);
  }

  executarSilentPing() {
    const inicio = performance.now();
    this.processoService.ping().subscribe({
      next: () => {
        const fim = performance.now();
        this.tempoLatenciaNuvem.set(Math.round(fim - inicio));
        this.statusNuvem.set('online');
      },
      error: () => {
        this.statusNuvem.set('aquecendo');
      }
    });
  }

  carregarHistorico() {
    try {
      const salvo = localStorage.getItem('ferreira_legal_historico');
      if (salvo) {
        this.historicoConsultas.set(JSON.parse(salvo));
      } else {
        this.historicoConsultas.set(['2101760-84.2025.8.26.0000', '5000001-05.2023.4.03.6100']);
      }
    } catch {
      this.historicoConsultas.set(['2101760-84.2025.8.26.0000', '5000001-05.2023.4.03.6100']);
    }
  }

  salvarNoHistorico(numero: string) {
    try {
      const atual = this.historicoConsultas().filter((n) => n !== numero);
      const novo = [numero, ...atual].slice(0, 6);
      this.historicoConsultas.set(novo);
      localStorage.setItem('ferreira_legal_historico', JSON.stringify(novo));
    } catch {}
  }

  // Watchlist Management
  carregarWatchlist() {
    try {
      const salvo = localStorage.getItem('ferreira_legal_watchlist');
      if (salvo) {
        this.watchlist.set(JSON.parse(salvo));
      }
    } catch {}
  }

  salvarWatchlist(itens: ProcessoWatchlistItem[]) {
    try {
      this.watchlist.set(itens);
      localStorage.setItem('ferreira_legal_watchlist', JSON.stringify(itens));
    } catch {}
  }

  alternarWatchlist() {
    const proc = this.processoAtual();
    if (!proc) return;

    const atual = this.watchlist();
    if (this.estaNaWatchlist()) {
      const novo = atual.filter((w) => w.numeroCnjLimpo !== proc.numeroCnjLimpo);
      this.salvarWatchlist(novo);
    } else {
      const item: ProcessoWatchlistItem = {
        numeroCnjFormatado: proc.numeroCnjFormatado,
        numeroCnjLimpo: proc.numeroCnjLimpo,
        tribunal: proc.tribunal,
        classe: proc.classe,
        dataSalvo: new Date().toISOString(),
        ultimoAndamentoTitulo: proc.movimentacoes[0]?.titulo,
        ultimoAndamentoData: proc.movimentacoes[0]?.dataHora,
        notificacaoPendente: false
      };
      this.salvarWatchlist([item, ...atual]);
      this.solicitarPermissaoNotificacao();
    }
  }

  solicitarPermissaoNotificacao() {
    if (typeof window !== 'undefined' && 'Notification' in window) {
      if (Notification.permission === 'default') {
        Notification.requestPermission();
      }
    }
  }

  abrirItemDaWatchlist(item: ProcessoWatchlistItem) {
    this.modoBusca.set('cnj');
    this.numeroProcessoInput.set(item.numeroCnjFormatado);
    this.watchlistPainelAberto.set(false);
    this.buscarProcesso();
  }

  // Ficha Institucional do Juízo / Magistrado + Notas Internas do Escritório
  abrirDossieMagistrado(nome: string) {
    const proc = this.processoAtual();
    const nomeLimpo = (nome || proc?.relator || proc?.orgaoJulgador || 'Juízo Competente').trim();
    this.carregarNotaEscritorio(nomeLimpo);

    this.processoService.obterMagistradoPorNome(nomeLimpo).subscribe({
      next: (mag) => {
        this.magistradoSelecionado.set(mag);
        this.magistradoModalAberto.set(true);
      },
      error: () => {
        // Sem perfilamento inventado: exibe apenas os dados institucionais objetivos do órgão/tribunal e abre espaço para notas do escritório
        const trib = proc?.tribunal || 'TJSP';
        const balcaoUrl =
          trib === 'TRF3'
            ? 'https://www.trf3.jus.br/balcao-virtual'
            : trib === 'STJ'
              ? 'https://www.stj.jus.br/sites/portalp/Processos/Balcao-Virtual'
              : 'https://www.tjsp.jus.br/BalcaoVirtual';

        this.magistradoSelecionado.set({
          id: `unid-${nomeLimpo.toLowerCase().replace(/\s+/g, '-')}`,
          nome: nomeLimpo,
          tituloFormatado: nomeLimpo,
          cargo: `Órgão Julgador / Relatoria (${trib})`,
          tribunal: trib,
          comarcaOuSecao: proc?.comarca || proc?.foro || `Jurisdição ${trib}`,
          varaOuCamara: proc?.orgaoJulgador || 'Unidade Judicial Competente',
          emailGabinete: 'Consultar Catálogo Oficial do Tribunal',
          telefoneCartorio: 'Consultar Lista Telefônica Oficial do Tribunal',
          balcaoVirtualUrl: balcaoUrl,
          horarioAtendimento: 'Atendimento telepresencial conforme expediente forense oficial do Tribunal',
          perfilGeral: 'Ficha Institucional da Unidade Judicial (Sem histórico estatístico pré-cadastrado)',
          tendenciaLiminares:
            'Sem amostragem estatística pré-calculada para este magistrado/órgão. Consulte as publicações do DJEN nestes autos ou registre abaixo as anotações do seu escritório.',
          tendenciaJusticaGratuita: 'Observância do Art. 99 do CPC/2015.',
          tendenciaHonorarios: 'Observância do Art. 85 do CPC/2015.',
          dicasPeticionamento: [
            'Utilizar o link oficial do Balcão Virtual do Tribunal acima para atendimento direto com o cartório ou gabinete.',
            'Registrar abaixo nas Notas Internas do Escritório os contatos verificados e observações práticas de audiências/despachos nesta unidade.'
          ],
          precedenteMarcante: proc?.documentosPublicos?.[0]?.ementa
        });
        this.magistradoModalAberto.set(true);
      }
    });
  }

  private obterChaveNotaMagistrado(nome: string): string {
    return `ferreira_nota_juizo_${nome.toLowerCase().replace(/[^a-z0-9]/g, '_')}`;
  }

  carregarNotaEscritorio(nome: string) {
    try {
      const chave = this.obterChaveNotaMagistrado(nome);
      this.notaEscritorioTexto.set(localStorage.getItem(chave) || '');
      this.notaSalvaFeedback.set(false);
    } catch {
      this.notaEscritorioTexto.set('');
    }
  }

  salvarNotaEscritorio() {
    const mag = this.magistradoSelecionado();
    if (!mag) return;
    try {
      const chave = this.obterChaveNotaMagistrado(mag.nome);
      localStorage.setItem(chave, this.notaEscritorioTexto());
      this.notaSalvaFeedback.set(true);
      setTimeout(() => this.notaSalvaFeedback.set(false), 2500);
    } catch {}
  }

  fecharDossieMagistrado() {
    this.magistradoModalAberto.set(false);
    this.magistradoSelecionado.set(null);
  }

  // Consulta Real de Carteira por OAB (DJEN/CNJ + e-SAJ, sem fallback hardcoded enganoso)
  buscarCarteiraOab(oab?: string) {
    const alvo = (oab || this.termoOabInput()).trim();
    if (!alvo) return;

    this.buscandoOab.set(true);
    this.erro.set(null);

    this.processoService.consultarCarteiraOab(alvo).subscribe({
      next: (cart) => {
        this.buscandoOab.set(false);
        this.advogadoCarteira.set(cart);
        if (cart.processos.length === 0) {
          this.erro.set(
            cart.avisosOuErros?.join(' ') ||
              `Nenhum processo público localizado para a OAB '${alvo}' nas bases DJEN/CNJ e e-SAJ.`
          );
        }
      },
      error: (err) => {
        this.buscandoOab.set(false);
        const msg =
          err?.error?.erro ||
          `Não foi possível consultar a carteira para a OAB '${alvo}' no momento. Verifique a conexão com a API ou tente novamente em instantes.`;
        this.erro.set(msg);
      }
    });
  }

  abrirProcessoDaCarteira(resumo: ProcessoResumo) {
    this.modoBusca.set('cnj');
    this.numeroProcessoInput.set(resumo.numeroCnjFormatado);
    this.buscarProcesso(false);
  }

  // Spotlight Controls
  abrirSpotlight() {
    this.spotlightTermo.set(this.numeroProcessoInput());
    this.spotlightAberto.set(true);
  }

  fecharSpotlight() {
    this.spotlightAberto.set(false);
  }

  selecionarDoHistorico(numero: string) {
    this.numeroProcessoInput.set(numero);
    this.spotlightTermo.set(numero);
    this.fecharSpotlight();
    this.buscarProcesso();
  }

  confirmarSpotlight() {
    this.numeroProcessoInput.set(this.spotlightTermo());
    this.fecharSpotlight();
    this.buscarProcesso();
  }

  formatarEntrada(event: Event) {
    const input = event.target as HTMLInputElement;
    this.numeroProcessoInput.set(input.value);
  }

  formatarEntradaSpotlight(event: Event) {
    const input = event.target as HTMLInputElement;
    this.spotlightTermo.set(input.value);
  }

  // Parser Matemático Módulo 97 do CNJ em Tempo Real
  decodificarCnj(raw: string): CnjDecodificado {
    const digitos = raw.replace(/\D/g, '');
    if (!digitos) {
      return { valido: null };
    }

    if (digitos.length !== 20) {
      return {
        valido: false,
        motivoErro: `Tamanho inválido (${digitos.length}/20 dígitos). O padrão CNJ deve conter exatamente 20 números.`
      };
    }

    const nnnnnnn = digitos.substring(0, 7);
    const dd = digitos.substring(7, 9);
    const aaaa = digitos.substring(9, 13);
    const j = digitos.substring(13, 14);
    const tr = digitos.substring(14, 16);
    const oooo = digitos.substring(16, 20);

    const formatado = `${nnnnnnn}-${dd}.${aaaa}.${j}.${tr}.${oooo}`;

    const ramos: Record<string, string> = {
      '1': 'Supremo Tribunal Federal (STF)',
      '2': 'Conselho Nacional de Justiça (CNJ)',
      '3': 'Superior Tribunal de Justiça (STJ)',
      '4': 'Justiça Federal (TRF)',
      '5': 'Justiça do Trabalho (TRT)',
      '6': 'Justiça Eleitoral (TRE)',
      '7': 'Justiça Militar da União',
      '8': 'Justiça dos Estados e DF (TJ)',
      '9': 'Justiça Militar Estadual'
    };

    let tribunalNome = `Tribunal ${tr}`;
    if (j === '8' && tr === '26') tribunalNome = 'Tribunal de Justiça de São Paulo (TJSP)';
    else if (j === '4' && tr === '03') tribunalNome = 'Tribunal Regional Federal da 3ª Região (TRF3)';
    else if (j === '4' && tr === '01') tribunalNome = 'TRF 1ª Região (TRF1)';

    const num1 = BigInt(nnnnnnn + aaaa + j + tr);
    const resto1 = num1 % 97n;
    const num2 = BigInt(resto1.toString() + oooo + '00');
    const resto2 = num2 % 97n;
    const dvEsperado = Number(97n - resto2);
    const dvInformado = parseInt(dd, 10);

    const valido = dvEsperado === dvInformado;

    return {
      valido,
      motivoErro: valido
        ? undefined
        : `Dígito verificador inválido (${dd}). O algoritmo Módulo 97 do CNJ calculou que deveria ser ${dvEsperado.toString().padStart(2, '0')}.`,
      sequencial: nnnnnnn,
      dv: dd,
      ano: aaaa,
      ramoJustica: ramos[j] || `Ramo ${j}`,
      tribunal: tribunalNome,
      origem:
        oooo === '0000'
          ? 'Tribunal (2ª Instância / Colegiado)'
          : `Unidade de Origem / Foro ${oooo}`,
      formatado
    };
  }

  iniciarRadar() {
    this.radarAtivo.set(true);
    this.radarEtapa.set(1);
    this.radarEtapaTexto.set('1/4: Validando máscara e dígito verificador Módulo 97 CNJ...');

    let etapa = 1;
    this.radarTimer = setInterval(() => {
      etapa++;
      if (etapa === 2) {
        this.radarEtapa.set(2);
        this.radarEtapaTexto.set('2/4: Consultando cluster em nuvem .NET 9 & MongoDB...');
      } else if (etapa === 3) {
        this.radarEtapa.set(3);
        this.radarEtapaTexto.set('3/4: Conectando às APIs Oficiais DataJud & DJEN/CNJ...');
      } else if (etapa === 4) {
        this.radarEtapa.set(4);
        this.radarEtapaTexto.set('4/4: Extraindo publicações, ementas e partes dos autos...');
      }
    }, 900);
  }

  pararRadar() {
    if (this.radarTimer) {
      clearInterval(this.radarTimer);
      this.radarTimer = null;
    }
    this.radarAtivo.set(false);
  }

  buscarProcesso(forcar = false, silencioso = false) {
    const raw = this.numeroProcessoInput().trim();
    if (!raw) return;

    const dec = this.decodificarCnj(raw);
    if (dec.valido === false) {
      this.erro.set(dec.motivoErro || 'Número de processo CNJ inválido.');
      return;
    }

    this.carregando.set(true);
    this.erro.set(null);
    this.prazoBaseCustomizado.set(null);
    if (!silencioso) {
      this.iniciarRadar();
    }

    this.processoService.consultarProcesso(raw, forcar).subscribe({
      next: (processo) => {
        this.pararRadar();
        this.processoAtual.set(processo);
        this.carregando.set(false);
        this.statusNuvem.set('online');
        this.salvarNoHistorico(processo.numeroCnjFormatado);

        // Ativa prazo em dobro automaticamente se Fazenda Pública / União constar no polo
        const temFazenda = (processo.envolvidos || []).some((e) => {
          const n = `${e.nome} ${e.oab || ''}`.toLowerCase();
          return (
            n.includes('fazenda') ||
            n.includes('união') ||
            n.includes('receita federal') ||
            n.includes('estado de') ||
            n.includes('município') ||
            n.includes('defensoria')
          );
        });
        this.prazoEmDobroAtivo.set(temFazenda);
      },
      error: (err) => {
        this.pararRadar();
        this.carregando.set(false);
        console.warn('Falha na resposta da API:', err);

        const limpo = raw.replace(/\D/g, '');

        if (err.error?.erro) {
          this.erro.set(err.error.erro);
        } else if (err.error?.mensagem) {
          this.erro.set(err.error.mensagem);
        } else if (limpo === '21017608420258260000' || limpo === '50000010520234036100') {
          this.statusNuvem.set('offline');
          this.carregarSnapshotDemonstracao(limpo);
        } else {
          this.statusNuvem.set('offline');
          this.erro.set(
            'Não foi possível se comunicar com o backend na nuvem. A instância no Render pode estar iniciando (Cold Start). Tente novamente em alguns segundos.'
          );
        }
      }
    });
  }

  // Snapshot Offline fiel aos registros oficiais do DJEN/CNJ e DataJud (acionado apenas se o container Render estiver em cold start)
  carregarSnapshotDemonstracao(limpo = '21017608420258260000') {
    if (limpo === '50000010520234036100') {
      const snapTrf3: Processo = {
        id: '8532880c-4e1f-4416-8550-49d3fe9930d4',
        numeroCnjFormatado: '5000001-05.2023.4.03.6100',
        numeroCnjLimpo: '50000010520234036100',
        tribunal: 'TRF3',
        grau: 1,
        classe: 'Mandado de Segurança Cível',
        assunto: 'Inquérito / Processo / Recurso Administrativo (Direito Tributário)',
        orgaoJulgador: '21ª VARA FEDERAL CÍVEL DE SÃO PAULO',
        comarca: 'Seção Judiciária de São Paulo (Federal)',
        foro: '21ª Vara Federal Cível de São Paulo (6100)',
        relator: 'Juízo Federal da 21ª Vara Cível de SP',
        valorCausa: 1000.0,
        segredoJustica: false,
        dataDistribuicao: '2023-01-02T12:00:00Z',
        status: 'Baixado Definitivamente (Transitado em Julgado)',
        ultimaAtualizacao: new Date().toISOString(),
        envolvidos: [
          {
            nome: 'Polo Ativo (Impetrante sob Proteção LGPD - Resolução CNJ nº 334/2020)',
            tipoPolo: 'Ativo',
            papel: 'Impetrante',
            oab: 'Patrono Habilitado nos Autos do PJe TRF3'
          },
          {
            nome: 'Delegado da Receita Federal do Brasil em São Paulo',
            tipoPolo: 'Passivo',
            papel: 'Autoridade Coatora',
            oab: 'Procuradoria-Geral da Fazenda Nacional (PGFN)'
          }
        ],
        documentosPublicos: [
          {
            id: 'doc-trf3-001',
            tipo: 'Sentença / Extinção sem Resolução do Mérito',
            data: '2023-03-16T12:00:00Z',
            titulo: 'Sentença Terminativa — Art. 485, IV do CPC',
            situacao: 'Transitado em Julgado',
            magistradoRelator: 'Juízo Federal da 21ª Vara Cível de SP',
            ementa:
              'MANDADO DE SEGURANÇA – AUSÊNCIA DE PROVA PRÉ-CONSTITUÍDA DE DIREITO LÍQUIDO E CERTO – NECESSIDADE DE DILAÇÃO PROBATÓRIA INCOMPATÍVEL COM O RITO DA LEI 12.016/2009 – EXTINÇÃO SEM RESOLUÇÃO DO MÉRITO.',
            conteudoTexto:
              'Julgo extinto o processo sem julgamento do mérito com fundamento no art. 485, IV do CPC.',
            certidaoPublicacao:
              'Disponibilizado no Diário da Justiça Eletrônico da 3ª Região em 17/03/2023. Baixa definitiva em 30/05/2023.',
            urlOrigem: 'https://pje1g.trf3.jus.br'
          }
        ],
        movimentacoes: [
          {
            dataHora: '2023-05-30T16:53:59Z',
            titulo: 'Baixa Definitiva',
            descricao: 'Baixa Definitiva dos autos arquivados no TRF3',
            codigoNacional: 22
          },
          {
            dataHora: '2023-05-29T11:17:03Z',
            titulo: 'Trânsito em julgado',
            descricao: 'Certificado o Trânsito em Julgado da Sentença',
            codigoNacional: 848
          },
          {
            dataHora: '2023-03-17T12:00:00Z',
            titulo: 'Disponibilização DJEN (TRF3)',
            descricao: 'Disponibilizado no Diário da Justiça Eletrônico do TRF3',
            codigoNacional: 92
          },
          {
            dataHora: '2023-03-16T17:25:22Z',
            titulo: 'Ausência das condições da ação',
            descricao: 'Sentença Extintiva Proferida (Art. 485, IV do CPC)',
            codigoNacional: 461
          },
          {
            dataHora: '2023-01-10T14:56:40Z',
            titulo: 'Requisição de Informações',
            descricao: 'Notificada a Autoridade Coatora para informações em 10 dias',
            codigoNacional: 11020
          },
          {
            dataHora: '2023-01-02T12:00:00Z',
            titulo: 'Distribuição',
            descricao: 'Distribuído por sorteio à 21ª Vara Federal Cível de SP',
            codigoNacional: 26
          }
        ]
      };
      this.processoAtual.set(snapTrf3);
      this.prazoEmDobroAtivo.set(true);
      this.erro.set(null);
      return;
    }

    const snapTjsp: Processo = {
      id: '1d5f1438-f818-4648-b0ef-ec39eaf6d8b5',
      numeroCnjFormatado: '2101760-84.2025.8.26.0000',
      numeroCnjLimpo: '21017608420258260000',
      tribunal: 'TJSP',
      grau: 2,
      classe: 'Agravo de Instrumento',
      assunto: 'Esbulho / Turbação / Ameaça (Posse)',
      orgaoJulgador: '24ª CÂMARA DE DIREITO PRIVADO',
      comarca: 'Comarca de Guarulhos / Foro de Guarulhos / 1ª Vara Cível',
      foro: '1ª Vara Cível do Foro de Guarulhos (Proc. 1º Grau: 1006805-85.2025.8.26.0224 - Juiz: Alex Freitas Lima)',
      processoOrigem: '1006805-85.2025.8.26.0224',
      juizOrigem: 'Alex Freitas Lima',
      relator: 'PEDRO PAULO MAILLET PREUSS',
      valorCausa: 10000.0,
      segredoJustica: false,
      dataDistribuicao: '2025-04-07T12:21:17Z',
      status: 'Julgado no TJSP • Em Grau de Recurso no STJ (AREsp 3319477/SP)',
      ultimaAtualizacao: new Date().toISOString(),
      envolvidos: [
        {
          nome: 'Espólio de Paschoal Thomeu',
          tipoPolo: 'Ativo',
          papel: 'Agravante',
          oab: 'João Paulo Paiva Camacho - OAB 238470SP'
        },
        {
          nome: 'Roseli Thomeu',
          tipoPolo: 'Ativo',
          papel: 'Agravante',
          oab: 'João Paulo Paiva Camacho - OAB 238470SP'
        },
        {
          nome: 'Maria Aparecida da Silva Batista',
          tipoPolo: 'Passivo',
          papel: 'Agravada',
          oab: 'Regina Celia do Carmo de Luca - OAB 84842RJ; Jose Gabriel Meritello do Carmo de Andrade Araujo - OAB 510501SP'
        },
        {
          nome: 'Arão Batista Antônio',
          tipoPolo: 'Passivo',
          papel: 'Agravado',
          oab: 'Regina Celia do Carmo de Luca - OAB 84842RJ; Jose Gabriel Meritello do Carmo de Andrade Araujo - OAB 510501SP'
        },
        {
          nome: 'Life Empreendimentos Ltda.',
          tipoPolo: 'Passivo',
          papel: 'Agravada',
          oab: 'Marcio Rogerio dos Santos Dias - OAB 131627SP; Marcelo Oliveira dos Santos - OAB 278202SP; Rafael Almeida Freire - OAB 529240SP'
        }
      ],
      documentosPublicos: [
        {
          id: 'djen-765092040',
          tipo: 'DJEN (STJ) - ATA DE DISTRIBUIÇÃO',
          data: '2026-10-23T12:00:00Z',
          titulo: 'ATA DE DISTRIBUIÇÃO — AGRAVO EM RECURSO ESPECIAL (STJ)',
          situacao: 'Publicado no DJEN/CNJ',
          magistradoRelator: 'MINISTRO ANTONIO CARLOS FERREIRA',
          ementa:
            'AREsp 3319477/SP (2026/0275833-5) — RELATOR: MINISTRO ANTONIO CARLOS FERREIRA. Processo distribuído pelo sistema automático em 22/10/2026.',
          conteudoTexto:
            'AREsp 3319477/SP (2026/0275833-5) - RELATOR: MINISTRO ANTONIO CARLOS FERREIRA - AGRAVANTE: PASCHOAL THOMEU (Repr. por ROSELI THOMEU) - ADVOGADO: JOÃO PAULO PAIVA CAMACHO - SP238470. Processo distribuído pelo sistema automático em 22/10/2026.',
          certidaoPublicacao:
            'Disponibilizado oficialmente no Diário de Justiça Eletrônico Nacional (DJEN/CNJ - ID 765092040) em 23/10/2026 pela Coordenadoria de Classificação e Distribuição de Processos (STJ).',
          urlOrigem:
            'https://justica.web.stj.jus.br/api/pcp/documentos/pdf/dinamico/NDA3MTY0NzExMjAyNjAyNzU4MzM1MTc5MjY2ODY4MDk0NA=='
        },
        {
          id: 'djen-421853952',
          tipo: 'DJEN (TJSP) - Intimação de Acórdão (Embargos)',
          data: '2025-10-02T12:00:00Z',
          titulo: 'Intimação de Acórdão — EMBARGOS DE DECLARAÇÃO CÍVEL (TJSP)',
          situacao: 'Rejeitaram os embargos. V. U.',
          magistradoRelator: 'PEDRO PAULO MAILLET PREUSS',
          ementa:
            'EMBARGOS DE DECLARAÇÃO. OMISSÃO, CONTRADIÇÃO E OBSCURIDADE. INOCORRÊNCIA. MERO INCONFORMISMO DA PARTE. AUSENTE QUALQUER VÍCIO NO DECISUM. INOCORRENTES AS HIPÓTESES DO ART. 1.022 DO CPC. RECURSO QUE, VIA DE REGRA, NÃO TEM CARÁTER INFRINGENTE. PREQUESTIONAMENTO. ART. 1.025, DO CPC. EMBARGOS REJEITADOS.',
          conteudoTexto: 'Rejeitaram os embargos. V. U. Magistrado(a) Pedro Paulo Maillet Preuss.',
          certidaoPublicacao:
            'Disponibilizado oficialmente no Diário de Justiça Eletrônico Nacional (DJEN/CNJ - ID 421853952) em 02/10/2025 pela 24ª Câmara de Direito Privado do TJSP.',
          urlOrigem: 'https://esaj.tjsp.jus.br/cposg/show.do?processo.codigo=RI008POZI0000'
        },
        {
          id: 'djen-370489236',
          tipo: 'DJEN (TJSP) - Intimação de Acórdão',
          data: '2025-09-04T12:00:00Z',
          titulo: 'Intimação de Acórdão — AGRAVO DE INSTRUMENTO (TJSP)',
          situacao: 'Deram provimento em parte ao recurso. V. U.',
          magistradoRelator: 'PEDRO PAULO MAILLET PREUSS',
          ementa:
            'AGRAVO DE INSTRUMENTO. AÇÃO DE MANUTENÇÃO DE POSSE. DECISÃO QUE DETERMINOU A CORREÇÃO DO VALOR DA CAUSA PARA QUE CORRESPONDA AO BENEFÍCIO PATRIMONIAL VISADO. INSURGÊNCIA DO AUTOR. CABIMENTO EM PARTE. ART. 292 QUE NÃO ESTABELECE CRITÉRIOS PARA O VALOR DA CAUSA EM AÇÕES POSSESSÓRIAS. ENTENDIMENTO CONSUETUDINÁRIO E JURISPRUDENCIAL DE QUE O VALOR DA CAUSA, NAS AÇÕES POSSESSÓRIAS, DEVA SER DE 1/3 DO VALOR VENAL DO IMÓVEL. PRECEDENTES. DECISÃO REFORMADA. RECURSO PARCIALMENTE PROVIDO.',
          conteudoTexto:
            'Deram provimento em parte ao recurso. V. U. Magistrado(a) Pedro Paulo Maillet Preuss.',
          certidaoPublicacao:
            'Disponibilizado oficialmente no Diário de Justiça Eletrônico Nacional (DJEN/CNJ - ID 370489236) em 04/09/2025 pela Subseção IX - Intimações de Acórdãos (TJSP).',
          urlOrigem: 'https://esaj.tjsp.jus.br/cposg/show.do?processo.codigo=RI008POZI0000'
        }
      ],
      movimentacoes: [
        {
          dataHora: '2026-10-23T12:00:00Z',
          titulo: 'Disponibilização DJEN (STJ) - ATA DE DISTRIBUIÇÃO',
          descricao:
            'AREsp 3319477/SP (2026/0275833-5) — Distribuído ao Relator Ministro Antonio Carlos Ferreira (DJEN ID 765092040)',
          codigoNacional: 92
        },
        {
          dataHora: '2026-10-21T12:00:00Z',
          titulo: 'Disponibilização DJEN (STJ) - DESPACHO / DECISÃO',
          descricao:
            'AREsp 3319477/SP — Despacho da Presidência do STJ (Min. Luis Felipe Salomão) determinando distribuição (DJEN ID 763473219)',
          codigoNacional: 92
        },
        {
          dataHora: '2026-07-06T10:49:20Z',
          titulo: 'Remessa',
          descricao: 'Remetidos os autos ao Setor de Recursos aos Tribunais Superiores',
          codigoNacional: 123
        },
        {
          dataHora: '2026-06-23T12:23:58Z',
          titulo: 'Ato ordinatório',
          descricao: 'Certificada a regularidade formal da juntada de contrarrazões',
          codigoNacional: 11383
        },
        {
          dataHora: '2026-06-22T11:33:00Z',
          titulo: 'Expedição de documento',
          descricao: 'Expedida notificação eletrônica via portal aos patronos',
          codigoNacional: 60
        },
        {
          dataHora: '2026-06-22T11:23:55Z',
          titulo: 'Decurso de Prazo',
          descricao: 'Decorrido prazo legal sem manifestação',
          codigoNacional: 1051
        },
        {
          dataHora: '2026-05-22T14:07:49Z',
          titulo: 'Petição',
          descricao: 'Juntada de Petição de Contrarrazões',
          codigoNacional: 85
        },
        {
          dataHora: '2026-05-19T12:00:00Z',
          titulo: 'Disponibilização DJEN (TJSP) - VISTA',
          descricao:
            'Vista à(s) parte(s) recorrida(s) para apresentação de contrarrazões (DJEN ID 614121539)',
          codigoNacional: 92
        },
        {
          dataHora: '2026-03-19T18:15:27Z',
          titulo: 'Recurso Especial',
          descricao: 'Interposição de Recurso Especial (STJ)',
          codigoNacional: 433
        },
        {
          dataHora: '2025-10-02T12:00:00Z',
          titulo: 'Disponibilização DJEN (TJSP) - Intimação de Acórdão',
          descricao:
            'Embargos de Declaração Cível rejeitados por votação unânime (DJEN ID 421853952)',
          codigoNacional: 92
        },
        {
          dataHora: '2025-09-04T12:00:00Z',
          titulo: 'Disponibilização DJEN (TJSP) - Intimação de Acórdão',
          descricao:
            'Acórdão publicado: Deram provimento em parte ao recurso quanto ao valor da causa em possessória (DJEN ID 370489236)',
          codigoNacional: 92
        },
        {
          dataHora: '2025-08-27T17:59:52Z',
          titulo: 'Julgamento',
          descricao: 'Sessão de Julgamento realizada: Deram provimento em parte ao recurso. V. U.',
          codigoNacional: 193
        },
        {
          dataHora: '2025-08-21T17:31:38Z',
          titulo: 'Pedido de inclusão em pauta virtual',
          descricao: 'Inclusão em pauta de julgamento virtual da 24ª Câmara',
          codigoNacional: 12313
        },
        {
          dataHora: '2025-07-24T17:16:27Z',
          titulo: 'Conclusão',
          descricao: 'Autos conclusos ao Relator Des. Pedro Paulo Maillet Preuss',
          codigoNacional: 51
        },
        {
          dataHora: '2025-04-10T12:00:00Z',
          titulo: 'Disponibilização DJEN (TJSP)',
          descricao: 'Disponibilizado no Diário da Justiça Eletrônico de São Paulo',
          codigoNacional: 92
        },
        {
          dataHora: '2025-04-07T14:06:55Z',
          titulo: 'Outras Decisões',
          descricao: 'Apreciação inicial no Agravo de Instrumento',
          codigoNacional: 12164
        },
        {
          dataHora: '2025-04-07T12:33:53Z',
          titulo: 'Conclusão',
          descricao: 'Autos conclusos ao Relator',
          codigoNacional: 51
        },
        {
          dataHora: '2025-04-07T12:21:17Z',
          titulo: 'Distribuição',
          descricao: 'Distribuído por sorteio à 24ª Câmara de Direito Privado',
          codigoNacional: 26
        }
      ]
    };

    this.processoAtual.set(snapTjsp);
    this.prazoEmDobroAtivo.set(false);
    this.erro.set(null);
  }

  abrirDocumento(doc: DocumentoProcessual) {
    this.documentoSelecionado.set(doc);
    this.visualizadorAberto.set(true);
  }

  fecharDocumento() {
    this.visualizadorAberto.set(false);
    this.documentoSelecionado.set(null);
  }

  imprimirCapa() {
    window.print();
  }

  imprimirDocumento() {
    window.print();
  }

  copiarNumero() {
    const proc = this.processoAtual();
    if (!proc) return;
    navigator.clipboard.writeText(proc.numeroCnjFormatado);
    this.copiado.set(true);
    setTimeout(() => this.copiado.set(false), 2000);
  }

  copiarEmenta() {
    const doc = this.documentoSelecionado();
    if (!doc?.ementa) return;
    navigator.clipboard.writeText(doc.ementa);
  }

  copiarRelatorioCliente() {
    const proc = this.processoAtual();
    const sintese = this.resumoExecutivoCliente();
    if (!proc || !sintese) return;
    const jur = this.jurimetriaDinamica();
    const risco = this.alertaRiscoForense();
    const prazo = this.termoFatalCalculado();

    const texto = `*RESUMO FORENSE EXECUTIVO - LEXFERREIRA* ⚖️
Olá! Segue a atualização oficial do seu processo:

📌 *Processo:* ${proc.numeroCnjFormatado}
🏛️ *Tribunal / Órgão:* ${proc.tribunal} (${proc.orgaoJulgador || 'Órgão Competente'})
⚖️ *Classe:* ${proc.classe || 'Ação Judicial'}
📊 *Situação Atual:* ${proc.status}
⚡ *Histórico Registrado:* ${jur?.totalMovimentacoes || proc.movimentacoes.length} movimentações e ${jur?.totalDocumentosDjen || 0} publicações DJEN

🔍 *Último Pronunciamento / Andamento:*
${sintese.oQueDecidiu}

📌 *Posição Processual:*
${sintese.impactoPratico}

🛡️ *Diagnóstico de Risco Forense:*
${risco?.titulo || 'Tramitação regular'}

⏱️ *Cálculo de Prazo (CPC/2015 + DJEN):*
${prazo ? `${prazo.tipoAto} — Vencimento em ${prazo.dataTermoFatal} (${prazo.diasUteisRestantes} dias úteis restantes)` : 'Nenhum prazo em aberto'}

🎯 *Próximo Passo:*
${sintese.proximoPasso}`;

    navigator.clipboard.writeText(texto);
    this.whatsappCopiado.set(true);
    setTimeout(() => this.whatsappCopiado.set(false), 3000);
  }
}
