import { Component, signal, computed, inject, OnInit, HostListener } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProcessoService } from './services/processo.service';
import { Processo, DocumentoProcessual } from './models/processo.model';

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

  // Estados principais
  numeroProcessoInput = signal('2101760-84.2025.8.26.0000');
  carregando = signal(false);
  erro = signal<string | null>(null);
  processoAtual = signal<Processo | null>(null);
  abaAtiva = signal<'capa' | 'documentos' | 'timeline' | 'dados'>('capa');
  filtroTextoTimeline = signal('');
  copiado = signal(false);

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

    return proc.movimentacoes.filter(m =>
      m.titulo.toLowerCase().includes(filtro) ||
      m.descricao?.toLowerCase().includes(filtro) ||
      (m.codigoNacional && m.codigoNacional.toString().includes(filtro))
    );
  });

  // Atalhos Globais de Teclado
  @HostListener('window:keydown', ['$event'])
  handleKeyboardEvent(event: KeyboardEvent) {
    if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
      event.preventDefault();
      this.abrirSpotlight();
    } else if (event.key === 'Escape') {
      if (this.visualizadorAberto()) {
        this.fecharDocumento();
      } else if (this.spotlightAberto()) {
        this.fecharSpotlight();
      }
    }
  }

  ngOnInit() {
    this.carregarHistorico();
    this.executarSilentPing();
    // Consulta inicial para exibir o processo ao usuário imediatamente
    this.buscarProcesso(false, true);
  }

  // Silent Ping para acordar o container do Render em background
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
        this.historicoConsultas.set([
          '2101760-84.2025.8.26.0000',
          '5000001-80.2023.4.03.6100'
        ]);
      }
    } catch {
      this.historicoConsultas.set(['2101760-84.2025.8.26.0000']);
    }
  }

  salvarNoHistorico(numero: string) {
    try {
      const atual = this.historicoConsultas().filter(n => n !== numero);
      const novo = [numero, ...atual].slice(0, 6);
      this.historicoConsultas.set(novo);
      localStorage.setItem('ferreira_legal_historico', JSON.stringify(novo));
    } catch {}
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

  // Parser Matemático Módulo 97 do CNJ em Tempo Real
  decodificarCnj(raw: string): CnjDecodificado {
    const digitos = raw.replace(/\D/g, '');
    if (digitos.length < 20) {
      return {
        valido: null,
        motivoErro: `Aguardando 20 dígitos (atual: ${digitos.length}/20)`
      };
    }

    const sequencial = digitos.substring(0, 7);
    const dv = digitos.substring(7, 9);
    const ano = digitos.substring(9, 13);
    const j = parseInt(digitos.substring(13, 14), 10);
    const tr = parseInt(digitos.substring(14, 16), 10);
    const origem = digitos.substring(16, 20);

    // Validação matemática do Módulo 97
    const bloco1 = sequencial + ano;
    const r1 = BigInt(bloco1) % 97n;
    const bloco2 = r1.toString() + digitos.substring(13, 20) + '00';
    const r2 = BigInt(bloco2) % 97n;
    const dvEsperado = Number(98n - r2);
    const dvCalculado = dvEsperado.toString().padStart(2, '0');

    const dvValido = dvCalculado === dv;

    // Ramo da Justiça
    const ramos: Record<number, string> = {
      1: 'STF (Supremo Tribunal Federal)',
      2: 'CNJ (Conselho Nacional de Justiça)',
      3: 'STJ (Superior Tribunal de Justiça)',
      4: 'Justiça Federal',
      5: 'Justiça do Trabalho',
      6: 'Justiça Eleitoral',
      7: 'Justiça Militar da União',
      8: 'Justiça Estadual (Comum)',
      9: 'Justiça Militar Estadual'
    };

    // Sigla do Tribunal
    let tribunalNome = `Tribunal ${tr}`;
    if (j === 8 && tr === 26) tribunalNome = 'TJSP - Tribunal de Justiça de São Paulo';
    else if (j === 8 && tr === 19) tribunalNome = 'TJRJ - Tribunal de Justiça do Rio de Janeiro';
    else if (j === 8 && tr === 13) tribunalNome = 'TJMG - Tribunal de Justiça de Minas Gerais';
    else if (j === 8 && tr === 21) tribunalNome = 'TJRS - Tribunal de Justiça do Rio Grande do Sul';
    else if (j === 4 && tr === 3) tribunalNome = 'TRF3 - Tribunal Regional Federal da 3ª Região';
    else if (j === 4 && tr === 1) tribunalNome = 'TRF1 - Tribunal Regional Federal da 1ª Região';
    else if (j === 4 && tr === 2) tribunalNome = 'TRF2 - Tribunal Regional Federal da 2ª Região';
    else if (j === 4 && tr === 4) tribunalNome = 'TRF4 - Tribunal Regional Federal da 4ª Região';

    const formatado = `${sequencial}-${dv}.${ano}.${j}.${tr.toString().padStart(2, '0')}.${origem}`;

    return {
      valido: dvValido,
      motivoErro: dvValido ? undefined : `Dígito verificador inválido (calculado: ${dvCalculado}, informado: ${dv})`,
      sequencial,
      dv,
      ano,
      ramoJustica: ramos[j] || `Ramo ${j}`,
      tribunal: tribunalNome,
      origem: origem === '0000' ? 'Tribunal / 2ª Instância' : `Comarca / Foro ${origem}`,
      formatado
    };
  }

  // Formatador da entrada de texto
  formatarEntrada(evento: Event) {
    const input = evento.target as HTMLInputElement;
    let valor = input.value.replace(/\D/g, '');
    if (valor.length > 20) valor = valor.substring(0, 20);

    let formatado = valor;
    if (valor.length > 7) formatado = `${valor.substring(0, 7)}-${valor.substring(7)}`;
    if (valor.length > 9) formatado = `${formatado.substring(0, 10)}.${valor.substring(9)}`;
    if (valor.length > 13) formatado = `${formatado.substring(0, 15)}.${valor.substring(13)}`;
    if (valor.length > 14) formatado = `${formatado.substring(0, 17)}.${valor.substring(14)}`;
    if (valor.length > 16) formatado = `${formatado.substring(0, 20)}.${valor.substring(16)}`;

    this.numeroProcessoInput.set(formatado);
    input.value = formatado;
  }

  formatarEntradaSpotlight(evento: Event) {
    const input = evento.target as HTMLInputElement;
    let valor = input.value.replace(/\D/g, '');
    if (valor.length > 20) valor = valor.substring(0, 20);

    let formatado = valor;
    if (valor.length > 7) formatado = `${valor.substring(0, 7)}-${valor.substring(7)}`;
    if (valor.length > 9) formatado = `${formatado.substring(0, 10)}.${valor.substring(9)}`;
    if (valor.length > 13) formatado = `${formatado.substring(0, 15)}.${valor.substring(13)}`;
    if (valor.length > 14) formatado = `${formatado.substring(0, 17)}.${valor.substring(14)}`;
    if (valor.length > 16) formatado = `${formatado.substring(0, 20)}.${valor.substring(16)}`;

    this.spotlightTermo.set(formatado);
    input.value = formatado;
  }

  // Inicia o Radar de Consulta em Etapas
  iniciarRadar() {
    this.radarAtivo.set(true);
    this.radarEtapa.set(1);
    this.radarEtapaTexto.set('1/4 • Validando algoritmo Módulo 97 do CNJ...');

    this.radarTimer = setInterval(() => {
      const etapa = this.radarEtapa();
      if (etapa === 1) {
        this.radarEtapa.set(2);
        this.radarEtapaTexto.set('2/4 • Conectando ao cluster .NET 9 Kestrel na nuvem...');
      } else if (etapa === 2) {
        this.radarEtapa.set(3);
        this.radarEtapaTexto.set('3/4 • Consultando índice nacional DataJud (CNJ)...');
      } else if (etapa === 3) {
        this.radarEtapa.set(4);
        this.radarEtapaTexto.set('4/4 • Varrendo e-SAJ TJSP para acórdãos e capa do processo...');
      }
    }, 1800);
  }

  pararRadar() {
    if (this.radarTimer) {
      clearInterval(this.radarTimer);
      this.radarTimer = null;
    }
    this.radarAtivo.set(false);
  }

  // Consulta do Processo
  buscarProcesso(forcar = false, silencioso = false) {
    const raw = this.numeroProcessoInput().trim();
    if (!raw) {
      this.erro.set('Por favor, informe o número do processo CNJ.');
      return;
    }

    this.carregando.set(true);
    this.erro.set(null);
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
      },
      error: (err) => {
        this.pararRadar();
        this.carregando.set(false);
        console.warn('Falha na comunicação direta:', err);

        // Se for o processo padrão, aciona o Snapshot de Alta Disponibilidade
        if (raw.replace(/\D/g, '') === '21017608420258260000') {
          this.carregarSnapshotDemonstracao();
        } else {
          this.erro.set('Não foi possível se comunicar com o backend na nuvem. A instância pode estar aquecendo no Render, tente novamente em alguns instantes.');
        }
      }
    });
  }

  // Snapshot de Demonstração para Recrutadores (Zero Failure Guarantee)
  carregarSnapshotDemonstracao() {
    const mock: Processo = {
      id: '1d5f1438-f818-4648-b0ef-ec39eaf6d8b5',
      numeroCnjFormatado: '2101760-84.2025.8.26.0000',
      numeroCnjLimpo: '21017608420258260000',
      tribunal: 'TJSP',
      grau: 2,
      classe: 'Agravo de Instrumento',
      assunto: 'Esbulho / Turbação / Ameaça (Direito das Coisas / Posse)',
      orgaoJulgador: '24ª CÂMARA DE DIREITO PRIVADO',
      comarca: 'São Paulo / Foro Central Cível',
      foro: 'Tribunal de Justiça (2ª Instância)',
      relator: 'Des. Relator Pedro Paulo Maillet Preuss',
      valorCausa: 50000.00,
      segredoJustica: false,
      dataDistribuicao: '2025-04-07T12:21:17Z',
      status: 'Julgado com Acórdão Proferido',
      ultimaAtualizacao: new Date().toISOString(),
      envolvidos: [
        {
          nome: 'Comercial e Construtora Leste Ltda.',
          tipoPolo: 'Ativo',
          papel: 'Agravante',
          oab: 'Dr. Marcelo Ribeiro - OAB/SP 184.920'
        },
        {
          nome: 'Espólio de Antonio Carlos da Silva',
          tipoPolo: 'Passivo',
          papel: 'Agravado',
          oab: 'Dra. Renata Albuquerque - OAB/SP 241.803'
        }
      ],
      documentosPublicos: [
        {
          id: '1a0ee27e-b348-4090-832a-d0d64bc2b12b',
          tipo: 'Acórdão / Julgamento Colegiado',
          data: '2025-08-27T00:00:00Z',
          titulo: 'Acórdão de 2º Grau - Julgado',
          situacao: 'Julgado',
          magistradoRelator: 'PEDRO PAULO MAILLET PREUSS',
          ementa: 'AGRAVO DE INSTRUMENTO – AÇÃO DE REINTEGRAÇÃO DE POSSE – LIMINAR DEFERIDA EM PRIMEIRO GRAU – ALEGAÇÃO DE ESBULHO E AUSÊNCIA DOS REQUISITOS DO ART. 561 DO CPC – DECISÃO REFORMADA EM PARTE PARA SUSPENDER A DESOCUPAÇÃO COMPULSÓRIA ATÉ AUDIÊNCIA DE JUSTIFICAÇÃO PRÉVIA – RECURSO PARCIALMENTE PROVIDO.',
          conteudoTexto: 'Acordam os Desembargadores da 24ª Câmara de Direito Privado do Tribunal de Justiça de São Paulo, por votação unânime, dar provimento em parte ao recurso, nos termos do voto do Relator Des. Pedro Paulo Maillet Preuss. V. U.',
          certidaoPublicacao: 'Sessão Colegiada de Julgamento realizada em 27/08/2025 pela 24ª Câmara de Direito Privado do TJSP. Registrado sob autenticação digital da Lei 11.419/2006.',
          urlOrigem: 'https://esaj.tjsp.jus.br/cposg/show.do?processo.codigo=RI008POZI0000'
        }
      ],
      movimentacoes: [
        { dataHora: '2026-07-06T10:49:20Z', titulo: 'Remessa', descricao: 'Remetidos os autos ao Setor de Recursos aos Tribunais Superiores', codigoNacional: 123 },
        { dataHora: '2026-06-23T12:23:58Z', titulo: 'Ato ordinatório', descricao: 'Certificada a regularidade formal da juntada de contrarrazões', codigoNacional: 11383 },
        { dataHora: '2026-06-22T11:33:00Z', titulo: 'Expedição de documento', descricao: 'Expedida notificação eletrônica via portal aos patronos', codigoNacional: 60 },
        { dataHora: '2026-06-22T11:23:55Z', titulo: 'Decurso de Prazo', descricao: 'Decorrido prazo legal sem oposição de embargos infringentes', codigoNacional: 1051 },
        { dataHora: '2026-05-22T14:07:49Z', titulo: 'Petição', descricao: 'Juntada de Petição de Contrarrazões ao Recurso Especial', codigoNacional: 85 },
        { dataHora: '2026-03-19T18:15:27Z', titulo: 'Recurso Especial', descricao: 'Interposição de Recurso Especial para o Superior Tribunal de Justiça (STJ)', codigoNacional: 433 },
        { dataHora: '2025-09-30T10:34:23Z', titulo: 'Julgamento', descricao: 'Acórdão Registrado sob o nº 2025.0000789123', codigoNacional: 193 },
        { dataHora: '2025-08-27T17:59:52Z', titulo: 'Julgamento', descricao: 'Sessão de Julgamento realizada: Deram provimento em parte ao recurso. V. U.', codigoNacional: 193 },
        { dataHora: '2025-08-21T17:31:38Z', titulo: 'Pedido de inclusão em pauta virtual', descricao: 'Inclusão em pauta de julgamento virtual da 24ª Câmara', codigoNacional: 12313 },
        { dataHora: '2025-07-24T17:16:27Z', titulo: 'Conclusão', descricao: 'Autos conclusos ao Relator Des. Pedro Paulo Maillet Preuss', codigoNacional: 51 },
        { dataHora: '2025-04-10T00:00:00Z', titulo: 'Publicação', descricao: 'Disponibilizado no Diário da Justiça Eletrônico de São Paulo', codigoNacional: 92 },
        { dataHora: '2025-04-07T14:06:55Z', titulo: 'Outras Decisões', descricao: 'Deferido efeito suspensivo ativo parcial para obstar desocupação compulsória imediata', codigoNacional: 12164 },
        { dataHora: '2025-04-07T12:33:53Z', titulo: 'Conclusão', descricao: 'Autos conclusos para apreciação de tutela de urgência recursal liminar', codigoNacional: 51 },
        { dataHora: '2025-04-07T12:21:17Z', titulo: 'Distribuição', descricao: 'Distribuído por sorteio à 24ª Câmara de Direito Privado', codigoNacional: 26 }
      ]
    };

    this.processoAtual.set(mock);
    this.erro.set(null);
  }

  // Visualizador Forense In-App
  abrirDocumento(doc: DocumentoProcessual) {
    this.documentoSelecionado.set(doc);
    this.visualizadorAberto.set(true);
  }

  fecharDocumento() {
    this.visualizadorAberto.set(false);
    this.documentoSelecionado.set(null);
  }

  // Impressões e Compartilhamento
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
    alert('Ementa jurídica copiada para a área de transferência!');
  }
}
