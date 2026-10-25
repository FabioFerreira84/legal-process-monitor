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
  ProcessoWatchlistItem,
  SuspensaoForenseLocal,
  TarefaProcessual,
  StatusTarefaColuna,
  PrioridadeTarefa,
  ItemRadarDjen,
  ConfigRadarOab,
  EstadoWorkspaceTenant,
  TeseJuridicaTenant,
  AplicacaoTeseProcesso,
  RecomendacaoTeseResult,
  FichaPatrimonialParte
} from './models/processo.model';
import {
  calcularTermoFatalForense,
  extrairDataCivilSegura,
  formatarDataBr,
  gerarConteudoIcsPrazoFatal,
  gerarLinkGoogleCalendarPrazoFatal,
  calcularDigitoVerificadorCnj
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
  abaAtiva = signal<'capa' | 'documentos' | 'timeline' | 'inteligencia' | 'kanban' | 'teses' | 'dados'>('capa');
  filtroTextoTimeline = signal('');
  copiado = signal(false);
  whatsappCopiado = signal(false);

  // ============================================================================
  // ESTADO MULTI-TENANT SaaS (ISOLAMENTO POR ESCRITÓRIO / WORKSPACE)
  // ============================================================================
  tenantIdAtivo = signal<string>('escritorio-ferreira');
  nomeEscritorioAtivo = signal<string>('Ferreira & Associados Advocacia');
  workspacesPreset = signal<{ id: string; nome: string; oab: string }[]>([
    { id: 'escritorio-ferreira', nome: 'Ferreira & Associados Advocacia', oab: '238470/SP' },
    { id: 'banca-corporativa-sp', nome: 'Banca Corporativa Paulista (Tenant B)', oab: '131627/SP' },
    { id: 'contencioso-tributario', nome: 'Contencioso Tributário Federal (Tenant C)', oab: '510501/SP' }
  ]);
  modalWorkspaceAberto = signal(false);
  novoTenantIdInput = signal('');
  novoTenantNomeInput = signal('');
  novoTenantOabInput = signal('238470/SP');
  webhookUrlInput = signal('');
  telegramBotTokenInput = signal('');
  telegramChatIdInput = signal('');
  salvandoConfigWorkspace = signal(false);
  feedbackWorkspaceMsg = signal<string | null>(null);

  // Suspensões Forenses Locais do Escritório (Provimentos CSM / Feriados do Interior)
  suspensoesLocais = signal<SuspensaoForenseLocal[]>([]);
  novaSuspensaoDataInput = signal('2026-10-29');
  novaSuspensaoDescricaoInput = signal('');
  formSuspensaoAberto = signal(false);

  // Quadro Kanban de Tarefas Processuais (Intimação -> Elaboração -> Revisão -> Protocolado)
  tarefasKanban = signal<TarefaProcessual[]>([]);
  novaTarefaTituloInput = signal('');
  novaTarefaResponsavelInput = signal('Dr. Fabio Ferreira');
  novaTarefaPrazoIsoInput = signal('2026-11-18');
  novaTarefaPrioridadeInput = signal<PrioridadeTarefa>(3);
  protocoloInputPorTarefa = signal<Record<string, string>>({});
  tarefaCriadaFeedback = signal(false);

  // Radar Proativo Diário DJEN & Alertas Webhook/Telegram
  itensRadar = signal<ItemRadarDjen[]>([]);
  radarDjenPainelAberto = signal(false);
  sincronizandoRadarDjen = signal(false);
  ultimoDiagnosticoRadar = signal<string | null>(null);
  notasJuizoTenant = signal<Record<string, string>>({});

  // Banco de Teses Jurídicas Multi-Tenant & Motor de Recomendação Tática
  tesesJuridicas = signal<TeseJuridicaTenant[]>([]);
  historicoAplicacoesTeses = signal<AplicacaoTeseProcesso[]>([]);
  formNovaTeseAberto = signal(false);
  novaTeseTituloInput = signal('');
  novaTeseAreaInput = signal('Processo Civil / Tribunais Superiores');
  novaTeseGatilhosInput = signal('AREsp, Súmula 7, Agravo');
  novaTeseFundamentacaoInput = signal('');
  novaTesePrecedenteInput = signal('');
  novaTeseModeloInput = signal('');
  teseAplicadaFeedback = signal<string | null>(null);
  teseCopiadaId = signal<string | null>(null);

  // Ficha Patrimonial / QSA da Parte Adversa + Sincronizador de Feriados
  fichaPatrimonialAberta = signal(false);
  carregandoFichaPatrimonial = signal(false);
  fichaPatrimonialSelecionada = signal<FichaPatrimonialParte | null>(null);
  cnpjCustomInput = signal('');
  sincronizandoFeriadosOficiais = signal(false);

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

  // 2. CALCULADORA DINÂMICA DE TERMO FATAL (CPC/2015 + LEI 14.759/2023 + DJEN RES. CNJ 455/2022 + SUSPENSÕES LOCAIS)
  termoFatalCalculado = computed<TermoFatalCalculado | null>(() => {
    const proc = this.processoAtual();
    const marco = this.marcoPublicacaoDetectado();
    if (!proc || !marco) return null;

    const diasBase = this.prazoBaseCustomizado() ?? marco.diasSugeridos;
    const suspensoes = this.suspensoesLocais().map((s) => ({
      data: s.data,
      descricao: s.descricao
    }));

    return calcularTermoFatalForense({
      dataMarco: marco.dataHora,
      diasPrazoBase: diasBase,
      ehDisponibilizacaoDjen: this.ehDisponibilizacaoDjenAtivo(),
      prazoEmDobro: this.prazoEmDobroAtivo(),
      siglaTribunal: proc.tribunal || 'TJSP',
      suspensoesLocais: suspensoes,
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
    const suspensoes = this.suspensoesLocais().map((s) => ({
      data: s.data,
      descricao: s.descricao
    }));

    return {
      embargos5d: calcularTermoFatalForense({
        dataMarco: marco.dataHora,
        diasPrazoBase: 5,
        ehDisponibilizacaoDjen: isDjen,
        prazoEmDobro: false,
        siglaTribunal: trib,
        suspensoesLocais: suspensoes,
        tipoAto: 'Embargos de Declaração'
      }),
      recurso15d: calcularTermoFatalForense({
        dataMarco: marco.dataHora,
        diasPrazoBase: 15,
        ehDisponibilizacaoDjen: isDjen,
        prazoEmDobro: false,
        siglaTribunal: trib,
        suspensoesLocais: suspensoes,
        tipoAto: 'Recurso / Contrarrazões'
      }),
      dobro30d: calcularTermoFatalForense({
        dataMarco: marco.dataHora,
        diasPrazoBase: 15,
        ehDisponibilizacaoDjen: isDjen,
        prazoEmDobro: true,
        siglaTribunal: trib,
        suspensoesLocais: suspensoes,
        tipoAto: 'Fazenda / Defensoria / MP (Dobro)'
      })
    };
  });

  // Link direto de 1 clique para Google Calendar
  urlGoogleCalendarPrazo = computed(() => {
    const proc = this.processoAtual();
    const calculo = this.termoFatalCalculado();
    if (!proc || !calculo) return 'https://calendar.google.com';

    return gerarLinkGoogleCalendarPrazoFatal({
      numeroProcesso: proc.numeroCnjFormatado,
      classeProcessual: proc.classe || 'Prazo Processual',
      orgaoJulgador: proc.orgaoJulgador || proc.tribunal,
      siglaTribunal: proc.tribunal || 'TJSP',
      calculo
    });
  });

  // Colunas do Quadro Kanban de Tarefas Processuais
  tarefasColunaIntimacao = computed(() => this.tarefasKanban().filter((t) => t.coluna === 0));
  tarefasColunaElaboracao = computed(() => this.tarefasKanban().filter((t) => t.coluna === 1));
  tarefasColunaRevisao = computed(() => this.tarefasKanban().filter((t) => t.coluna === 2));
  tarefasColunaProtocolado = computed(() => this.tarefasKanban().filter((t) => t.coluna === 3));
  totalRadarNaoLidas = computed(() => this.itensRadar().filter((i) => !i.lida).length);

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

  // 5. MOTOR DE RECOMENDAÇÃO TÁTICA DE TESES (CRUZA O QUE ESTÁ ACONTECENDO NO PROCESSO COM O BANCO DE TESES DO ESCRITÓRIO)
  recomendacoesTesesAtuais = computed<RecomendacaoTeseResult[]>(() => {
    const proc = this.processoAtual();
    const teses = this.tesesJuridicas();
    const historico = this.historicoAplicacoesTeses();
    if (!proc || !teses || teses.length === 0) return [];

    const norm = (s?: string | null) =>
      (s || '')
        .toLowerCase()
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '');

    const cnjLimpo = (proc.numeroCnjLimpo || proc.numeroCnjFormatado || '').replace(/\D/g, '');
    const ultMov = proc.movimentacoes?.[0];
    const topMovs = (proc.movimentacoes || [])
      .slice(0, 6)
      .map((m) => `${m.titulo} ${m.descricao}`)
      .join(' | ');
    const topDocs = (proc.documentosPublicos || [])
      .slice(0, 4)
      .map((d) => `${d.tipo} ${d.titulo} ${d.ementa}`)
      .join(' | ');

    const corpus = norm(
      `${proc.tribunal} ${proc.classe} ${proc.assunto} ${proc.orgaoJulgador} ${proc.status} ${topMovs} ${topDocs}`
    );
    const recente = norm(
      `${ultMov?.titulo || ''} ${ultMov?.descricao || ''} ${proc.classe || ''} ${proc.assunto || ''}`
    );

    const lista: RecomendacaoTeseResult[] = teses.map((tese) => {
      let score = 0;
      const termosDetectados: string[] = [];

      for (const gat of tese.eventosGatilho || []) {
        const gNorm = norm(gat);
        if (!gNorm) continue;
        if (recente.includes(gNorm)) {
          score += 32;
          termosDetectados.push(gat);
        } else if (corpus.includes(gNorm)) {
          score += 18;
          termosDetectados.push(gat);
        } else {
          const tokens = gNorm.split(/\s+/).filter((tk) => tk.length >= 4);
          if (tokens.length > 0 && tokens.every((tk) => corpus.includes(tk))) {
            score += 22;
            termosDetectados.push(gat);
          }
        }
      }

      const area = norm(tese.areaDireito);
      if (corpus.includes('tributar') && area.includes('tributar')) score += 18;
      if (
        (corpus.includes('esbulho') || corpus.includes('posse') || corpus.includes('possessor')) &&
        (area.includes('possessor') || area.includes('civil') || area.includes('agronegocio'))
      ) {
        score += 18;
      }
      if (
        (corpus.includes('aresp') || corpus.includes('resp') || corpus.includes('stj') || corpus.includes('agravo')) &&
        (area.includes('superiores') || area.includes('processo civil') || area.includes('recursal'))
      ) {
        score += 16;
      }
      if (
        (corpus.includes('sisbajud') || corpus.includes('penhora') || corpus.includes('execucao')) &&
        area.includes('execu')
      ) {
        score += 20;
      }

      const jaUsada = historico.some(
        (h) =>
          h.teseId === tese.id &&
          h.numeroProcesso.replace(/\D/g, '') === cnjLimpo &&
          cnjLimpo.length > 0
      );

      if (jaUsada) score += 15;
      else if (tese.totalUsos > 0) score += Math.min(8, tese.totalUsos * 2);

      score = Math.max(12, Math.min(99, score));
      const nivelAderencia =
        score >= 75
          ? 'Recomendação Crítica (Alta Aderência)'
          : score >= 45
            ? 'Recomendada para o Ato Atual'
            : 'Tese Subsidiária / Preventiva';

      let motivo = '';
      if (jaUsada) {
        motivo += 'Tese já aplicada anteriormente nestes autos pelo escritório. ';
      }
      if (termosDetectados.length > 0) {
        motivo += `Enquadramento direto detectado pelos gatilhos [${termosDetectados.slice(0, 4).join(', ')}] diante do andamento "${ultMov?.titulo || proc.classe}". `;
      } else {
        motivo += `Tese de apoio compatível com ${proc.classe || 'o feito'} (${tese.areaDireito}). `;
      }
      if (tese.precedenteChave) {
        motivo += `Precedente: ${tese.precedenteChave}.`;
      }

      return {
        tese,
        scoreAderencia: score,
        nivelAderencia,
        motivoEnquadramento: motivo.trim(),
        termosDetectadosNoProcesso: termosDetectados,
        jaUtilizadaNesteProcesso: jaUsada
      };
    });

    return lista.sort((a, b) => b.scoreAderencia - a.scoreAderencia || b.tese.totalUsos - a.tese.totalUsos);
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
      } else if (this.radarDjenPainelAberto()) {
        this.radarDjenPainelAberto.set(false);
      } else if (this.modalWorkspaceAberto()) {
        this.modalWorkspaceAberto.set(false);
      }
    }
  }

  ngOnInit() {
    this.carregarTenantSalvo();
    this.carregarHistorico();
    this.carregarWorkspaceMultiTenant(this.tenantIdAtivo());
    this.executarSilentPing();
    this.buscarProcesso(false, true);
  }

  // ============================================================================
  // GESTÃO MULTI-TENANT SaaS (ISOLAMENTO POR ESCRITÓRIO NO SQLITE + CONTINGÊNCIA LOCAL)
  // ============================================================================
  private obterChaveLocalTenant(sufixo: string, tenantId = this.tenantIdAtivo()): string {
    return `ferreira_saas_${tenantId}_${sufixo}`;
  }

  carregarTenantSalvo() {
    try {
      const salvo = localStorage.getItem('ferreira_saas_tenant_ativo');
      if (salvo && /^[a-zA-Z0-9_-]{3,64}$/.test(salvo)) {
        this.tenantIdAtivo.set(salvo);
      }
    } catch {}
  }

  selecionarTenantWorkspace(tenantId: string) {
    const limpo = (tenantId || '')
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9_-]/g, '-');
    if (limpo.length < 3) return;

    this.tenantIdAtivo.set(limpo);
    try {
      localStorage.setItem('ferreira_saas_tenant_ativo', limpo);
    } catch {}

    const preset = this.workspacesPreset().find((p) => p.id === limpo);
    if (preset) {
      this.nomeEscritorioAtivo.set(preset.nome);
      this.novoTenantOabInput.set(preset.oab);
    } else {
      this.nomeEscritorioAtivo.set(`Escritório ${limpo}`);
    }

    this.carregarWorkspaceMultiTenant(limpo);
  }

  criarOuAlternarWorkspaceCustomizado() {
    const idBruto = this.novoTenantIdInput().trim().toLowerCase().replace(/[^a-z0-9_-]/g, '-');
    if (idBruto.length < 3) return;

    const nome = this.novoTenantNomeInput().trim() || `Escritório ${idBruto}`;
    const oab = this.novoTenantOabInput().trim() || '238470/SP';

    const listaAtual = this.workspacesPreset();
    if (!listaAtual.some((w) => w.id === idBruto)) {
      this.workspacesPreset.set([...listaAtual, { id: idBruto, nome, oab }]);
    }

    this.selecionarTenantWorkspace(idBruto);
    this.salvarConfiguracaoWorkspace();
    this.novoTenantIdInput.set('');
    this.novoTenantNomeInput.set('');
  }

  carregarWorkspaceMultiTenant(tenantId: string) {
    // 1. Hidratação imediata do cache isolado do tenant
    this.hidratarContingenciaLocalTenant(tenantId);

    // 2. Sincronização com o banco SQLite Multi-Tenant no Backend
    this.processoService.obterEstadoWorkspace(tenantId).subscribe({
      next: (estado: EstadoWorkspaceTenant) => {
        this.aplicarEstadoWorkspace(estado);
      },
      error: () => {
        // Mantém estado isolado por tenant já carregado
      }
    });
  }

  private aplicarEstadoWorkspace(estado: EstadoWorkspaceTenant) {
    const tId = estado.tenantId;
    if (estado.configuracao) {
      this.nomeEscritorioAtivo.set(estado.configuracao.nomeEscritorio || `Workspace ${tId}`);
      if (estado.configuracao.inscricoesOab?.length > 0) {
        this.novoTenantOabInput.set(estado.configuracao.inscricoesOab.join(', '));
      }
      this.webhookUrlInput.set(estado.configuracao.webhookUrl || '');
      this.telegramChatIdInput.set(estado.configuracao.telegramChatId || '');
    }

    const wl: ProcessoWatchlistItem[] = (estado.watchlist || []).map((w) => ({
      numeroCnjFormatado: w.numeroProcesso,
      numeroCnjLimpo: w.numeroProcesso.replace(/\D/g, ''),
      tribunal: w.tribunal,
      classe: w.classe,
      dataSalvo: w.adicionadoEmUtc,
      ultimoAndamentoTitulo: `${w.totalMovimentacoesConhecidas} movimentações monitoradas`,
      notificacaoPendente: w.temNovidade
    }));

    this.watchlist.set(wl);
    this.notasJuizoTenant.set(estado.notasJuizo || {});
    this.suspensoesLocais.set(estado.suspensoesLocais || []);
    this.tarefasKanban.set(estado.tarefasKanban || []);
    this.itensRadar.set(estado.itensRadar || []);
    if (estado.tesesJuridicas) this.tesesJuridicas.set(estado.tesesJuridicas);
    if (estado.historicoAplicacoesTeses) {
      this.historicoAplicacoesTeses.set(estado.historicoAplicacoesTeses);
    }
    this.persistirContingenciaLocalTenant(tId);
  }

  private obterTesesPadraoDemo(): TeseJuridicaTenant[] {
    return [
      {
        id: 'tese-stj-distinguishing-sumula-7',
        tenantId: 'escritorio-ferreira',
        titulo: 'Impugnação ao AREsp — Súmula 7/STJ vs. Revaloração Jurídica e Súmula 182/STJ',
        areaDireito: 'Processo Civil / Tribunais Superiores (STJ)',
        eventosGatilho: ['AREsp', 'REsp', 'STJ', 'Agravo em Recurso Especial', 'Contrarrazões', 'Distribuição', 'Súmula 7'],
        fundamentacaoLegal: 'Art. 1.029 e Art. 1.030, V, do CPC/2015; Súmulas 5, 7 e 182 do STJ',
        precedenteChave: 'STJ — AREsp 3319477/SP (Presidência Min. Antonio Carlos Ferreira) e Corte Especial (Súmula 182/STJ)',
        modeloArgumentacao:
          '1. Preliminar de Não Conhecimento (Súmula 182/STJ): a agravante limitou-se a repetir as razões do Recurso Especial sem infirmar especificamente todos os capítulos da decisão de inadmissibilidade.\n2. Óbice da Súmula 7/STJ: a reversão do acórdão recorrido demandaria reexame fático-probatório, vedado na via especial.',
        totalUsos: 4,
        ultimoProcessoAplicado: '2101760-84.2025.8.26.0000',
        atualizadoEmUtc: new Date().toISOString()
      },
      {
        id: 'tese-possessoria-requisitos-561-cpc',
        tenantId: 'escritorio-ferreira',
        titulo: 'Tutela Possessória de Urgência — Requisitos Cumulativos do Art. 561 do CPC e Periculum in Mora Inverso',
        areaDireito: 'Direito Civil / Possessórias e Agronegócio',
        eventosGatilho: ['Esbulho', 'Turbação', 'Ameaça', 'Agravo de Instrumento', 'Reintegração de Posse', 'Liminar', 'Art. 561'],
        fundamentacaoLegal: 'Arts. 300, 560, 561 e 562 do CPC/2015; Art. 1.210 do Código Civil',
        precedenteChave: 'TJSP — 24ª Câmara de Direito Privado (Agravo nº 2101760-84.2025.8.26.0000 — Rel. Des. Pedro Paulo Maillet Preuss)',
        modeloArgumentacao:
          'A concessão ou reforma de liminar possessória exige prova documental inequívoca da posse anterior e do esbulho ou turbação há menos de ano e dia (Art. 561, I a IV, do CPC), sob pena de periculum in mora inverso na atividade cooperativa agrícola.',
        totalUsos: 7,
        ultimoProcessoAplicado: '2101760-84.2025.8.26.0000',
        atualizadoEmUtc: new Date().toISOString()
      },
      {
        id: 'tese-ms-tributario-prova-pre-constituida',
        tenantId: 'escritorio-ferreira',
        titulo: 'Mandado de Segurança Tributário — Direito Líquido e Certo, Tema 118/STJ e Art. 485, IV do CPC',
        areaDireito: 'Direito Tributário / Processo Constitucional',
        eventosGatilho: ['Mandado de Segurança', 'Tributário', 'Receita Federal', 'TRF3', 'Ausência das condições da ação', 'Art. 485'],
        fundamentacaoLegal: 'Art. 5º, LXIX, CF/88; Lei nº 12.016/2009; Súmula 213/STJ; Tema Repetitivo 118/STJ',
        precedenteChave: 'STJ — Tema Repetitivo 118 (REsp 1.111.164/BA) e Súmula 213/STJ',
        modeloArgumentacao:
          'Nos termos da Súmula 213/STJ e do Tema 118/STJ, para fins de declaração do direito à compensação tributária via Mandado de Segurança, basta a prova documental da condição de credor tributário, apurando-se o quantum na via administrativa.',
        totalUsos: 3,
        ultimoProcessoAplicado: '5000001-05.2023.4.03.6100',
        atualizadoEmUtc: new Date().toISOString()
      },
      {
        id: 'tese-execucao-sisbajud-menor-onerosidade',
        tenantId: 'escritorio-ferreira',
        titulo: 'Desbloqueio de Ativos SISBAJUD — Proteção do Capital de Giro Essencial e Menor Onerosidade (Art. 805 CPC)',
        areaDireito: 'Execução Civil / Empresarial',
        eventosGatilho: ['SISBAJUD', 'Penhora', 'Bloqueio', 'Execução', 'Cumprimento de Sentença', 'Art. 805'],
        fundamentacaoLegal: 'Art. 805, parágrafo único, Art. 835, § 2º, e Art. 854, § 3º, do CPC/2015',
        precedenteChave: 'STJ — Corte Especial (EREsp 1.874.222/DF): Proteção do capital de giro indispensável à continuidade empresarial',
        modeloArgumentacao:
          'Requer-se o desbloqueio imediato do excesso via SISBAJUD (Art. 854, § 3º, CPC) ou substituição por seguro garantia judicial acrescido de 30% (Art. 835, § 2º, CPC), preservando-se a folha de pagamento e a operação.',
        totalUsos: 5,
        ultimoProcessoAplicado: '1006805-85.2025.8.26.0224',
        atualizadoEmUtc: new Date().toISOString()
      }
    ];
  }

  private hidratarContingenciaLocalTenant(tenantId: string) {
    try {
      const raw = localStorage.getItem(this.obterChaveLocalTenant('estado', tenantId));
      if (raw) {
        const parsed = JSON.parse(raw);
        this.watchlist.set(parsed.watchlist || []);
        this.notasJuizoTenant.set(parsed.notasJuizo || {});
        this.suspensoesLocais.set(parsed.suspensoesLocais || []);
        this.tarefasKanban.set(parsed.tarefasKanban || []);
        this.itensRadar.set(parsed.itensRadar || []);
        if (parsed.tesesJuridicas && parsed.tesesJuridicas.length > 0) {
          this.tesesJuridicas.set(parsed.tesesJuridicas);
        } else if (tenantId === 'escritorio-ferreira') {
          this.tesesJuridicas.set(this.obterTesesPadraoDemo());
        }
        this.historicoAplicacoesTeses.set(parsed.historicoAplicacoesTeses || []);
        if (parsed.nomeEscritorio) this.nomeEscritorioAtivo.set(parsed.nomeEscritorio);
        return;
      }
    } catch {}

    // Se for o escritório padrão e ainda não houver cache, popula estado inicial real
    if (tenantId === 'escritorio-ferreira') {
      this.watchlist.set([
        {
          numeroCnjFormatado: '2101760-84.2025.8.26.0000',
          numeroCnjLimpo: '21017608420258260000',
          tribunal: 'TJSP',
          classe: 'Agravo de Instrumento',
          dataSalvo: new Date().toISOString(),
          ultimoAndamentoTitulo: 'Disponibilização DJEN (STJ) - ATA DE DISTRIBUIÇÃO',
          notificacaoPendente: true
        }
      ]);
      this.tarefasKanban.set([
        {
          id: 'kanban-stj-aresp-3319477',
          tenantId: 'escritorio-ferreira',
          numeroProcesso: '2101760-84.2025.8.26.0000',
          titulo: 'Apresentar Contrarrazões / Memorial no AREsp 3319477/SP (STJ)',
          descricao:
            'Intimação DJEN ID 765092040 de 23/10/2026 — Distribuído ao Min. Antonio Carlos Ferreira.',
          responsavel: 'Dr. Fabio Ferreira (OAB/SP 238.470)',
          coluna: 0,
          prioridade: 3,
          prazoFatal: '2026-11-18',
          diasPrazo: 15,
          prazoEmDobro: false,
          numeroProtocolo: null,
          criadoEmUtc: new Date().toISOString(),
          atualizadoEmUtc: new Date().toISOString()
        }
      ]);
      this.itensRadar.set([
        {
          id: 'djen-765092040',
          tenantId: 'escritorio-ferreira',
          oabOrigem: '238470/SP',
          numeroProcesso: '2101760-84.2025.8.26.0000',
          tribunal: 'STJ',
          orgaoJulgador: 'Presidência do STJ',
          dataDisponibilizacao: '2026-10-23T12:00:00Z',
          tipoComunicacao: 'Intimação de Distribuição (AREsp 3319477/SP)',
          teorResumo:
            'AREsp 3319477/SP (2026/0275833-5) — Distribuído em 22/10/2026 ao Min. Antonio Carlos Ferreira (DJEN ID 765092040).',
          linkInteiroTeor:
            'https://justica.web.stj.jus.br/api/pcp/documentos/pdf/dinamico/NDA3MTY0NzExMjAyNjAyNzU4MzM1MTc5MjY2ODY4MDk0NA==',
          lida: false,
          convertidaEmTarefa: true,
          capturadoEmUtc: new Date().toISOString()
        }
      ]);
      this.suspensoesLocais.set([]);
      this.notasJuizoTenant.set({});
      this.tesesJuridicas.set(this.obterTesesPadraoDemo());
      this.historicoAplicacoesTeses.set([
        {
          id: 'aplic-demo-001',
          tenantId: 'escritorio-ferreira',
          teseId: 'tese-possessoria-requisitos-561-cpc',
          tituloTese:
            'Tutela Possessória de Urgência — Requisitos Cumulativos do Art. 561 do CPC e Periculum in Mora Inverso',
          numeroProcesso: '2101760-84.2025.8.26.0000',
          eventoContexto: 'Julgamento Colegiado na 24ª Câmara de Direito Privado do TJSP',
          resultadoEsperadoOuObtido:
            'Tese acolhida no Acórdão de 2º Grau — Recurso da parte adversa improvido por unanimidade',
          responsavel: 'Dr. Fabio Ferreira (OAB/SP 238.470)',
          aplicadaEmUtc: '2025-10-15T14:00:00Z'
        }
      ]);
    } else {
      // Qualquer outro tenant inicia completamente limpo e isolado
      this.watchlist.set([]);
      this.tarefasKanban.set([]);
      this.itensRadar.set([]);
      this.suspensoesLocais.set([]);
      this.notasJuizoTenant.set({});
      this.tesesJuridicas.set([]);
      this.historicoAplicacoesTeses.set([]);
    }
  }

  private persistirContingenciaLocalTenant(tenantId = this.tenantIdAtivo()) {
    try {
      const payload = {
        nomeEscritorio: this.nomeEscritorioAtivo(),
        watchlist: this.watchlist(),
        notasJuizo: this.notasJuizoTenant(),
        suspensoesLocais: this.suspensoesLocais(),
        tarefasKanban: this.tarefasKanban(),
        itensRadar: this.itensRadar(),
        tesesJuridicas: this.tesesJuridicas(),
        historicoAplicacoesTeses: this.historicoAplicacoesTeses()
      };
      localStorage.setItem(this.obterChaveLocalTenant('estado', tenantId), JSON.stringify(payload));
    } catch {}
  }

  salvarConfiguracaoWorkspace() {
    const tenant = this.tenantIdAtivo();
    const oabs = this.novoTenantOabInput()
      .split(/[,;]/)
      .map((s) => s.trim())
      .filter((s) => s.length > 0);

    this.salvandoConfigWorkspace.set(true);
    this.feedbackWorkspaceMsg.set(null);

    this.processoService
      .atualizarConfigWorkspace(tenant, {
        nomeEscritorio: this.nomeEscritorioAtivo(),
        inscricoesOab: oabs,
        webhookUrl: this.webhookUrlInput().trim() || null,
        telegramBotToken: this.telegramBotTokenInput().trim() || null,
        telegramChatId: this.telegramChatIdInput().trim() || null,
        ativo: true
      })
      .subscribe({
        next: () => {
          this.salvandoConfigWorkspace.set(false);
          this.persistirContingenciaLocalTenant(tenant);
          this.feedbackWorkspaceMsg.set(
            'Configurações Multi-Tenant e canais de alerta salvos no banco SQLite!'
          );
          setTimeout(() => this.feedbackWorkspaceMsg.set(null), 3500);
        },
        error: (err) => {
          this.salvandoConfigWorkspace.set(false);
          this.persistirContingenciaLocalTenant(tenant);
          this.feedbackWorkspaceMsg.set(
            err?.error?.mensagem || 'Configurações salvas localmente no Workspace.'
          );
        }
      });
  }

  // ============================================================================
  // SUSPENSÕES FORENSES LOCAIS & EXPORTADOR AGENDA .ICS (RFC 5545)
  // ============================================================================
  adicionarSuspensaoLocal() {
    const dataIso = this.novaSuspensaoDataInput().trim();
    const descricao = this.novaSuspensaoDescricaoInput().trim();
    if (!dataIso || !descricao) return;

    const proc = this.processoAtual();
    const comarca = proc?.tribunal || 'TJSP';
    const tenant = this.tenantIdAtivo();

    const tempItem: SuspensaoForenseLocal = {
      id: `susp-${Date.now()}`,
      tenantId: tenant,
      data: dataIso,
      descricao,
      comarcaOuTribunal: comarca,
      criadoEmUtc: new Date().toISOString()
    };

    this.suspensoesLocais.set([...this.suspensoesLocais(), tempItem]);
    this.novaSuspensaoDescricaoInput.set('');
    this.persistirContingenciaLocalTenant(tenant);

    this.processoService
      .adicionarSuspensaoLocal(tenant, { dataIso, descricao, comarcaOuTribunal: comarca })
      .subscribe({
        next: (salva) => {
          this.suspensoesLocais.set(
            this.suspensoesLocais().map((s) => (s.id === tempItem.id ? salva : s))
          );
          this.persistirContingenciaLocalTenant(tenant);
        },
        error: () => {}
      });
  }

  removerSuspensaoLocal(id: string) {
    const tenant = this.tenantIdAtivo();
    this.suspensoesLocais.set(this.suspensoesLocais().filter((s) => s.id !== id));
    this.persistirContingenciaLocalTenant(tenant);

    this.processoService.removerSuspensaoLocal(tenant, id).subscribe({ error: () => {} });
  }

  exportarPrazoIcs() {
    const proc = this.processoAtual();
    const calculo = this.termoFatalCalculado();
    if (!proc || !calculo) return;

    const conteudoIcs = gerarConteudoIcsPrazoFatal({
      numeroProcesso: proc.numeroCnjFormatado,
      classeProcessual: proc.classe || 'Prazo Processual',
      orgaoJulgador: proc.orgaoJulgador || proc.tribunal,
      siglaTribunal: proc.tribunal || 'TJSP',
      calculo,
      responsavel: this.nomeEscritorioAtivo()
    });

    const blob = new Blob([conteudoIcs], { type: 'text/calendar;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `prazo-fatal-${proc.numeroCnjLimpo}.ics`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  }

  // ============================================================================
  // QUADRO KANBAN DE TAREFAS PROCESSUAIS (INTIMAÇÃO -> ELABORAÇÃO -> REVISÃO -> PROTOCOLADO)
  // ============================================================================
  criarTarefaAPartirDaCalculadora() {
    const proc = this.processoAtual();
    const calculo = this.termoFatalCalculado();
    if (!proc || !calculo) return;

    const partes = calculo.dataTermoFatal.split('/');
    const prazoIso =
      partes.length === 3 ? `${partes[2]}-${partes[1]}-${partes[0]}` : '2026-11-18';

    const tenant = this.tenantIdAtivo();
    const nova: TarefaProcessual = {
      id: `task-${Date.now()}`,
      tenantId: tenant,
      numeroProcesso: proc.numeroCnjFormatado,
      titulo: `${calculo.tipoAto} — ${proc.classe || 'Processo'}`,
      descricao: `Prazo calculado: ${calculo.diasUteisTotal} dias úteis (Publicação DJEN: ${calculo.dataPublicacaoDje} • Vencimento Fatal: ${calculo.dataTermoFatal}).`,
      responsavel: this.novaTarefaResponsavelInput() || 'Advogado Responsável',
      coluna: 0,
      prioridade: calculo.diasUteisRestantes <= 5 ? 3 : 2,
      prazoFatal: prazoIso,
      diasPrazo: calculo.diasUteisTotal,
      prazoEmDobro: calculo.prazoEmDobro,
      numeroProtocolo: null,
      criadoEmUtc: new Date().toISOString(),
      atualizadoEmUtc: new Date().toISOString()
    };

    this.tarefasKanban.set([nova, ...this.tarefasKanban()]);
    this.persistirContingenciaLocalTenant(tenant);
    this.tarefaCriadaFeedback.set(true);
    setTimeout(() => this.tarefaCriadaFeedback.set(false), 2800);

    this.processoService
      .salvarTarefaKanban(tenant, {
        id: nova.id,
        numeroProcesso: nova.numeroProcesso,
        titulo: nova.titulo,
        descricao: nova.descricao,
        responsavel: nova.responsavel,
        coluna: nova.coluna,
        prioridade: nova.prioridade,
        prazoFatalIso: nova.prazoFatal,
        diasPrazo: nova.diasPrazo,
        prazoEmDobro: nova.prazoEmDobro
      })
      .subscribe({ error: () => {} });
  }

  adicionarTarefaManualKanban() {
    const titulo = this.novaTarefaTituloInput().trim();
    if (!titulo) return;

    const proc = this.processoAtual();
    const tenant = this.tenantIdAtivo();
    const nova: TarefaProcessual = {
      id: `task-${Date.now()}`,
      tenantId: tenant,
      numeroProcesso: proc?.numeroCnjFormatado || this.numeroProcessoInput(),
      titulo,
      descricao: `Tarefa vinculada ao órgão ${proc?.orgaoJulgador || proc?.tribunal || 'TJSP'}.`,
      responsavel: this.novaTarefaResponsavelInput().trim() || 'Advogado Responsável',
      coluna: 0,
      prioridade: this.novaTarefaPrioridadeInput(),
      prazoFatal: this.novaTarefaPrazoIsoInput() || '2026-11-18',
      diasPrazo: 15,
      prazoEmDobro: false,
      numeroProtocolo: null,
      criadoEmUtc: new Date().toISOString(),
      atualizadoEmUtc: new Date().toISOString()
    };

    this.tarefasKanban.set([nova, ...this.tarefasKanban()]);
    this.novaTarefaTituloInput.set('');
    this.persistirContingenciaLocalTenant(tenant);

    this.processoService
      .salvarTarefaKanban(tenant, {
        id: nova.id,
        numeroProcesso: nova.numeroProcesso,
        titulo: nova.titulo,
        descricao: nova.descricao,
        responsavel: nova.responsavel,
        coluna: nova.coluna,
        prioridade: nova.prioridade,
        prazoFatalIso: nova.prazoFatal,
        diasPrazo: nova.diasPrazo,
        prazoEmDobro: nova.prazoEmDobro
      })
      .subscribe({ error: () => {} });
  }

  moverTarefaKanban(tarefa: TarefaProcessual, novaColuna: StatusTarefaColuna) {
    const tenant = this.tenantIdAtivo();
    const protocoloDigitado = this.protocoloInputPorTarefa()[tarefa.id]?.trim();
    const protocoloFinal =
      novaColuna === 3
        ? protocoloDigitado || tarefa.numeroProtocolo || `PROT-${Date.now().toString().slice(-7)}`
        : tarefa.numeroProtocolo;

    const atualizada: TarefaProcessual = {
      ...tarefa,
      coluna: novaColuna,
      numeroProtocolo: protocoloFinal,
      atualizadoEmUtc: new Date().toISOString()
    };

    this.tarefasKanban.set(
      this.tarefasKanban().map((t) => (t.id === tarefa.id ? atualizada : t))
    );
    this.persistirContingenciaLocalTenant(tenant);

    this.processoService
      .salvarTarefaKanban(tenant, {
        id: atualizada.id,
        numeroProcesso: atualizada.numeroProcesso,
        titulo: atualizada.titulo,
        descricao: atualizada.descricao,
        responsavel: atualizada.responsavel,
        coluna: atualizada.coluna,
        prioridade: atualizada.prioridade,
        prazoFatalIso: atualizada.prazoFatal,
        diasPrazo: atualizada.diasPrazo,
        prazoEmDobro: atualizada.prazoEmDobro,
        numeroProtocolo: atualizada.numeroProtocolo
      })
      .subscribe({ error: () => {} });
  }

  atualizarProtocoloInput(tarefaId: string, valor: string) {
    this.protocoloInputPorTarefa.set({
      ...this.protocoloInputPorTarefa(),
      [tarefaId]: valor
    });
  }

  removerTarefaKanban(id: string) {
    const tenant = this.tenantIdAtivo();
    this.tarefasKanban.set(this.tarefasKanban().filter((t) => t.id !== id));
    this.persistirContingenciaLocalTenant(tenant);
    this.processoService.removerTarefaKanban(tenant, id).subscribe({ error: () => {} });
  }

  // ============================================================================
  // BANCO DE TESES JURÍDICAS MULTI-TENANT & MOTOR DE RECOMENDAÇÃO TÁTICA
  // ============================================================================
  salvarNovaTeseJuridica() {
    const titulo = this.novaTeseTituloInput().trim();
    if (!titulo) return;

    const tenant = this.tenantIdAtivo();
    const gatilhos = this.novaTeseGatilhosInput()
      .split(/[,;]/)
      .map((g) => g.trim())
      .filter((g) => g.length > 0);

    const nova: TeseJuridicaTenant = {
      id: `tese-${Date.now()}`,
      tenantId: tenant,
      titulo,
      areaDireito: this.novaTeseAreaInput().trim() || 'Contencioso Cível',
      eventosGatilho: gatilhos.length > 0 ? gatilhos : ['Manifestação', 'Recurso'],
      fundamentacaoLegal: this.novaTeseFundamentacaoInput().trim() || 'CPC/2015',
      precedenteChave: this.novaTesePrecedenteInput().trim() || 'Jurisprudência Dominante STJ',
      modeloArgumentacao:
        this.novaTeseModeloInput().trim() ||
        'Requer-se o acolhimento da tese com base nos precedentes vinculantes invocados.',
      totalUsos: 0,
      ultimoProcessoAplicado: null,
      atualizadoEmUtc: new Date().toISOString()
    };

    this.tesesJuridicas.set([nova, ...this.tesesJuridicas()]);
    this.novaTeseTituloInput.set('');
    this.novaTeseFundamentacaoInput.set('');
    this.novaTesePrecedenteInput.set('');
    this.novaTeseModeloInput.set('');
    this.formNovaTeseAberto.set(false);
    this.persistirContingenciaLocalTenant(tenant);

    this.processoService
      .salvarTeseJuridica(tenant, {
        id: nova.id,
        titulo: nova.titulo,
        areaDireito: nova.areaDireito,
        eventosGatilho: nova.eventosGatilho,
        fundamentacaoLegal: nova.fundamentacaoLegal,
        precedenteChave: nova.precedenteChave,
        modeloArgumentacao: nova.modeloArgumentacao
      })
      .subscribe({
        next: (salva) => {
          this.tesesJuridicas.set(
            this.tesesJuridicas().map((t) => (t.id === nova.id ? salva : t))
          );
          this.persistirContingenciaLocalTenant(tenant);
        },
        error: () => {}
      });
  }

  removerTeseJuridica(teseId: string) {
    const tenant = this.tenantIdAtivo();
    this.tesesJuridicas.set(this.tesesJuridicas().filter((t) => t.id !== teseId));
    this.persistirContingenciaLocalTenant(tenant);
    this.processoService.removerTeseJuridica(tenant, teseId).subscribe({ error: () => {} });
  }

  copiarModeloArgumentacaoTese(tese: TeseJuridicaTenant) {
    const proc = this.processoAtual();
    const cabecalho = proc
      ? `PROCESSO CNJ: ${proc.numeroCnjFormatado} (${proc.orgaoJulgador || proc.tribunal})\n`
      : '';
    const texto = `${cabecalho}TESE: ${tese.titulo}\nFUNDAMENTAÇÃO LEGAL: ${tese.fundamentacaoLegal}\nPRECEDENTE-CHAVE: ${tese.precedenteChave}\n\nMINUTA DE ARGUMENTAÇÃO:\n${tese.modeloArgumentacao}`;
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(texto);
    }
    this.teseCopiadaId.set(tese.id);
    setTimeout(() => this.teseCopiadaId.set(null), 2500);
  }

  aplicarTeseNoProcessoAtual(tese: TeseJuridicaTenant, criarNoKanban = true) {
    const proc = this.processoAtual();
    const tenant = this.tenantIdAtivo();
    const numeroProc = proc?.numeroCnjFormatado || this.numeroProcessoInput() || '2101760-84.2025.8.26.0000';
    const ultMov = proc?.movimentacoes?.[0]?.titulo || 'Manifestação Processual';
    const calculo = this.termoFatalCalculado();
    const partes = (calculo?.dataTermoFatal || '18/11/2026').split('/');
    const prazoIso =
      partes.length === 3 ? `${partes[2]}-${partes[1]}-${partes[0]}` : '2026-11-18';

    // Copia automaticamente o modelo da tese para o clipboard do advogado
    this.copiarModeloArgumentacaoTese(tese);

    const novaAplic: AplicacaoTeseProcesso = {
      id: `aplic-${Date.now()}`,
      tenantId: tenant,
      teseId: tese.id,
      tituloTese: tese.titulo,
      numeroProcesso: numeroProc,
      eventoContexto: `Aplicada diante do andamento: ${ultMov}`,
      resultadoEsperadoOuObtido: criarNoKanban
        ? 'Vinculada ao Quadro Kanban em Elaboração'
        : 'Registrada no histórico estratégico dos autos',
      responsavel: this.novaTarefaResponsavelInput() || 'Dr. Fabio Ferreira',
      aplicadaEmUtc: new Date().toISOString()
    };

    this.historicoAplicacoesTeses.set([novaAplic, ...this.historicoAplicacoesTeses()]);
    this.tesesJuridicas.set(
      this.tesesJuridicas().map((t) =>
        t.id === tese.id
          ? {
              ...t,
              totalUsos: t.totalUsos + 1,
              ultimoProcessoAplicado: numeroProc,
              atualizadoEmUtc: new Date().toISOString()
            }
          : t
      )
    );

    if (criarNoKanban) {
      const novaTarefa: TarefaProcessual = {
        id: `tese-task-${Date.now()}`,
        tenantId: tenant,
        numeroProcesso: numeroProc,
        titulo: `Peticionar com Tese: ${tese.titulo}`,
        descricao: `Fundamentação: ${tese.fundamentacaoLegal} | Precedente: ${tese.precedenteChave}`,
        responsavel: novaAplic.responsavel,
        coluna: 1,
        prioridade: 3,
        prazoFatal: prazoIso,
        diasPrazo: calculo?.diasUteisTotal || 15,
        prazoEmDobro: calculo?.prazoEmDobro || false,
        numeroProtocolo: null,
        criadoEmUtc: new Date().toISOString(),
        atualizadoEmUtc: new Date().toISOString()
      };
      this.tarefasKanban.set([novaTarefa, ...this.tarefasKanban()]);
    }

    this.persistirContingenciaLocalTenant(tenant);
    this.teseAplicadaFeedback.set(
      `Tese "${tese.titulo}" vinculada ao processo ${numeroProc} (argumentação copiada para a área de transferência${criarNoKanban ? ' + card criado no Kanban' : ''})!`
    );
    setTimeout(() => this.teseAplicadaFeedback.set(null), 4500);

    this.processoService
      .aplicarTeseNoProcesso(tenant, {
        teseId: tese.id,
        numeroProcesso: numeroProc,
        eventoContexto: novaAplic.eventoContexto,
        resultadoEsperadoOuObtido: novaAplic.resultadoEsperadoOuObtido,
        responsavel: novaAplic.responsavel,
        criarTarefaKanban: criarNoKanban,
        prazoFatalIso: prazoIso
      })
      .subscribe({
        next: (res) => {
          if (res.teses?.length) this.tesesJuridicas.set(res.teses);
          this.persistirContingenciaLocalTenant(tenant);
        },
        error: () => {}
      });
  }

  // ============================================================================
  // FICHA PATRIMONIAL / QSA DA PARTE ADVERSA + FERIADOS OFICIAIS (BRASILAPI + QUERIDO DIÁRIO)
  // ============================================================================
  abrirFichaPatrimonialParte(nomeOuCnpj?: string) {
    const proc = this.processoAtual();
    const termo = (nomeOuCnpj || this.cnpjCustomInput() || proc?.envolvidos?.[0]?.nome || '').trim();
    if (!termo) return;

    this.fichaPatrimonialAberta.set(true);
    this.carregandoFichaPatrimonial.set(true);
    const tenant = this.tenantIdAtivo();

    this.processoService
      .consultarFichaPatrimonialParte(tenant, termo, proc?.comarca || proc?.foro)
      .subscribe({
        next: (ficha) => {
          this.carregandoFichaPatrimonial.set(false);
          this.fichaPatrimonialSelecionada.set(ficha);
        },
        error: () => {
          this.carregandoFichaPatrimonial.set(false);
          this.fichaPatrimonialSelecionada.set({
            termoConsultado: termo,
            cnpjFormatado: null,
            razaoSocial: termo,
            nomeFantasia: termo,
            situacaoCadastral: 'Consulta offline — informe o CNPJ de 14 dígitos ou tente novamente',
            capitalSocial: 0,
            porteEmpresa: 'Informar CNPJ para extração QSA',
            naturezaJuridica: 'Parte Processual',
            atividadePrincipalCnae: 'Consulte com a API ativa para extrair QSA na Receita Federal via BrasilAPI.',
            enderecoDiligenciaCitacao: proc?.comarca || proc?.foro || 'Ver autos',
            municipioUf: proc?.tribunal || 'SP',
            quadroSocietario: [],
            mencoesDiariosMunicipais: [],
            fonteDados: 'Contingência Local',
            consultadoEmUtc: new Date().toISOString()
          });
        }
      });
  }

  sincronizarFeriadosForensesOficiais() {
    const tenant = this.tenantIdAtivo();
    const trib = this.processoAtual()?.tribunal || 'TJSP';
    this.sincronizandoFeriadosOficiais.set(true);

    this.processoService.sincronizarFeriadosOficiais(tenant, 2026, trib).subscribe({
      next: (res) => {
        this.sincronizandoFeriadosOficiais.set(false);
        this.suspensoesLocais.set(res.suspensoesLocais || []);
        this.persistirContingenciaLocalTenant(tenant);
      },
      error: () => {
        this.sincronizandoFeriadosOficiais.set(false);
      }
    });
  }

  // ============================================================================
  // RADAR PROATIVO DIÁRIO DJEN & DISPARO DE WEBHOOK / TELEGRAM
  // ============================================================================
  sincronizarRadarDjenAgora() {
    const tenant = this.tenantIdAtivo();
    const oabPrimaria = this.novoTenantOabInput().split(/[,;]/)[0]?.trim() || '238470/SP';
    this.sincronizandoRadarDjen.set(true);
    this.ultimoDiagnosticoRadar.set(null);

    this.processoService.sincronizarRadarDjen(tenant, oabPrimaria, true).subscribe({
      next: (res) => {
        this.sincronizandoRadarDjen.set(false);
        this.itensRadar.set(res.itensRadar || []);
        this.persistirContingenciaLocalTenant(tenant);
        const msgAlerta = res.alerta ? ` • ${res.alerta.mensagemDiagnostico}` : '';
        this.ultimoDiagnosticoRadar.set(
          `Radar DJEN sincronizado (${res.novasPublicacoesInseridas} novas, ${res.totalNoRadar} no total)${msgAlerta}`
        );
      },
      error: () => {
        this.sincronizandoRadarDjen.set(false);
        this.ultimoDiagnosticoRadar.set(
          'Exibindo publicações DJEN em cache do Workspace (API em aquecimento).'
        );
      }
    });
  }

  converterItemRadarEmTarefa(item: ItemRadarDjen) {
    const tenant = this.tenantIdAtivo();
    this.processoService
      .converterRadarEmTarefa(tenant, item.id, {
        diasPrazoBase: 15,
        prazoEmDobro: false,
        responsavel: `OAB ${item.oabOrigem}`
      })
      .subscribe({
        next: (res) => {
          this.tarefasKanban.set([
            res.tarefa,
            ...this.tarefasKanban().filter((t) => t.id !== res.tarefa.id)
          ]);
          this.itensRadar.set(
            this.itensRadar().map((i) =>
              i.id === item.id ? { ...i, lida: true, convertidaEmTarefa: true } : i
            )
          );
          this.persistirContingenciaLocalTenant(tenant);
          this.radarDjenPainelAberto.set(false);
          this.abaAtiva.set('kanban');
        },
        error: () => {
          // Fallback local imediato se offline
          const nova: TarefaProcessual = {
            id: `radar-task-${item.id}`,
            tenantId: tenant,
            numeroProcesso: item.numeroProcesso,
            titulo: `Cumprir Intimação DJEN (${item.tipoComunicacao})`,
            descricao: item.teorResumo,
            responsavel: `OAB ${item.oabOrigem}`,
            coluna: 0,
            prioridade: 3,
            prazoFatal: '2026-11-18',
            diasPrazo: 15,
            prazoEmDobro: false,
            numeroProtocolo: null,
            criadoEmUtc: new Date().toISOString(),
            atualizadoEmUtc: new Date().toISOString()
          };
          this.tarefasKanban.set([nova, ...this.tarefasKanban()]);
          this.itensRadar.set(
            this.itensRadar().map((i) =>
              i.id === item.id ? { ...i, lida: true, convertidaEmTarefa: true } : i
            )
          );
          this.persistirContingenciaLocalTenant(tenant);
          this.radarDjenPainelAberto.set(false);
          this.abaAtiva.set('kanban');
        }
      });
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

    const tenant = this.tenantIdAtivo();
    const atual = this.watchlist();
    if (this.estaNaWatchlist()) {
      const novo = atual.filter((w) => w.numeroCnjLimpo !== proc.numeroCnjLimpo);
      this.watchlist.set(novo);
      this.persistirContingenciaLocalTenant(tenant);
      this.processoService
        .removerWatchlistTenant(tenant, proc.numeroCnjFormatado)
        .subscribe({ error: () => {} });
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
      this.watchlist.set([item, ...atual]);
      this.persistirContingenciaLocalTenant(tenant);
      this.solicitarPermissaoNotificacao();
      this.processoService
        .salvarWatchlistTenant(tenant, {
          numeroProcesso: proc.numeroCnjFormatado,
          classe: proc.classe || 'Processo Judicial',
          orgaoJulgador: proc.orgaoJulgador || proc.tribunal,
          tribunal: proc.tribunal,
          totalMovimentacoesConhecidas: proc.movimentacoes?.length || 0,
          temNovidade: false
        })
        .subscribe({ error: () => {} });
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
    const trib = proc?.tribunal || 'TJSP';
    this.carregarNotaEscritorio(nomeLimpo);

    this.processoService.obterMagistradoPorNome(nomeLimpo, trib).subscribe({
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
    return `${this.tenantIdAtivo()}::${nome.toLowerCase().replace(/[^a-z0-9]/g, '_')}`;
  }

  carregarNotaEscritorio(nome: string) {
    try {
      const chave = this.obterChaveNotaMagistrado(nome);
      const notaDoTenant = this.notasJuizoTenant()[chave];
      if (notaDoTenant !== undefined) {
        this.notaEscritorioTexto.set(notaDoTenant);
      } else {
        this.notaEscritorioTexto.set(localStorage.getItem(`ferreira_nota_${chave}`) || '');
      }
      this.notaSalvaFeedback.set(false);
    } catch {
      this.notaEscritorioTexto.set('');
    }
  }

  salvarNotaEscritorio() {
    const mag = this.magistradoSelecionado();
    if (!mag) return;
    try {
      const tenant = this.tenantIdAtivo();
      const chave = this.obterChaveNotaMagistrado(mag.nome);
      const texto = this.notaEscritorioTexto();
      localStorage.setItem(`ferreira_nota_${chave}`, texto);
      this.notasJuizoTenant.set({ ...this.notasJuizoTenant(), [chave]: texto });
      this.persistirContingenciaLocalTenant(tenant);
      this.notaSalvaFeedback.set(true);
      setTimeout(() => this.notaSalvaFeedback.set(false), 2500);

      this.processoService
        .salvarNotaJuizoTenant(tenant, chave, texto)
        .subscribe({ error: () => {} });
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

    const checkDv = calcularDigitoVerificadorCnj(digitos);
    const valido = checkDv.valido;

    return {
      valido,
      motivoErro: valido
        ? undefined
        : `Dígito verificador inválido (${dd}). O algoritmo Módulo 97 do CNJ calculou que deveria ser ${checkDv.dvEsperado}.`,
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
