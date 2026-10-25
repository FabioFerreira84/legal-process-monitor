export interface Movimentacao {
  dataHora: string;
  titulo: string;
  descricao: string;
  codigoNacional?: number;
  complementos?: Record<string, string>;
}

export interface Envolvido {
  nome: string;
  tipoPolo: string;
  papel?: string;
  oab?: string;
  documento?: string;
}

export interface DocumentoProcessual {
  id: string;
  tipo: string;
  data: string;
  titulo: string;
  conteudoTexto: string;
  situacao?: string;
  magistradoRelator?: string;
  ementa?: string;
  certidaoPublicacao?: string;
  urlOrigem?: string;
}

export interface Processo {
  id: string;
  numeroCnjFormatado: string;
  numeroCnjLimpo: string;
  tribunal: string;
  grau: number;
  classe?: string;
  assunto?: string;
  orgaoJulgador?: string;
  dataDistribuicao?: string;
  status: string;
  ultimaAtualizacao: string;
  comarca?: string;
  foro?: string;
  valorCausa?: number;
  relator?: string;
  processoOrigem?: string;
  juizOrigem?: string;
  segredoJustica?: boolean;
  movimentacoes: Movimentacao[];
  envolvidos: Envolvido[];
  documentosPublicos?: DocumentoProcessual[];
}

export interface JulgamentoRealMagistrado {
  numeroProcesso: string;
  tribunal: string;
  classe: string;
  orgaoJulgador: string;
  dataPublicacao: string;
  ementaResumo: string;
  fonteOficial: string;
  urlOrigem: string;
}

export interface PerfilMagistrado {
  id: string;
  nome: string;
  tituloFormatado: string;
  cargo: string;
  tribunal: string;
  comarcaOuSecao: string;
  varaOuCamara: string;
  emailGabinete?: string;
  telefoneCartorio?: string;
  balcaoVirtualUrl?: string;
  horarioAtendimento?: string;
  perfilGeral: string;
  tendenciaLiminares: string;
  tendenciaJusticaGratuita: string;
  tendenciaHonorarios: string;
  dicasPeticionamento: string[];
  precedenteMarcante?: string;
  julgamentosReaisRecentes?: JulgamentoRealMagistrado[];
}

export interface ProcessoResumo {
  numeroCnjFormatado: string;
  numeroCnjLimpo: string;
  tribunal: string;
  grau: number;
  classe: string;
  foro?: string;
  vara?: string;
  papel?: string;
  dataRecebimento?: string;
  urlOrigem?: string;
  fonteDados?: string;
  ultimaComunicacaoResumo?: string;
}

export interface AdvogadoCarteira {
  numeroOab: string;
  nomeAdvogado?: string;
  tribunal: string;
  totalProcessos: number;
  totalComunicacoesDjen?: number;
  fontesConsultadas?: string[];
  avisosOuErros?: string[];
  dataConsulta: string;
  processos: ProcessoResumo[];
}

export interface RiscoForense {
  nivel: 'critico' | 'atencao' | 'favoravel' | 'informativo';
  titulo: string;
  descricao: string;
  termoDetectado: string;
  recomendacaoTatica: string;
  dataEvento: string;
}

export interface TermoFatalCalculado {
  dataDisponibilizacaoDjen: string;
  dataPublicacaoDje: string;
  dataInicioPrazo: string;
  dataTermoFatal: string;
  diasPrazoBase: number;
  prazoEmDobro: boolean;
  ehDisponibilizacaoDjen: boolean;
  diasUteisTotal: number;
  diasUteisRestantes: number;
  precluso: boolean;
  baseLegal: string;
  tipoAto: string;
  feriadosConsiderados: { data: string; descricao: string }[];
}

export interface ProcessoWatchlistItem {
  numeroCnjFormatado: string;
  numeroCnjLimpo: string;
  tribunal: string;
  classe?: string;
  dataSalvo: string;
  ultimoAndamentoTitulo?: string;
  ultimoAndamentoData?: string;
  notificacaoPendente?: boolean;
}

export type StatusTarefaColuna = 0 | 1 | 2 | 3; // 0=Intimacao, 1=Elaboracao, 2=Revisao, 3=Protocolado
export type PrioridadeTarefa = 0 | 1 | 2 | 3; // 0=Baixa, 1=Media, 2=Alta, 3=Critica

export interface SuspensaoForenseLocal {
  id: string;
  tenantId: string;
  data: string; // YYYY-MM-DD
  descricao: string;
  comarcaOuTribunal: string;
  criadoEmUtc: string;
}

export interface TarefaProcessual {
  id: string;
  tenantId: string;
  numeroProcesso: string;
  titulo: string;
  descricao: string;
  responsavel: string;
  coluna: StatusTarefaColuna;
  prioridade: PrioridadeTarefa;
  prazoFatal: string; // YYYY-MM-DD
  diasPrazo: number;
  prazoEmDobro: boolean;
  numeroProtocolo?: string | null;
  criadoEmUtc: string;
  atualizadoEmUtc: string;
}

export interface ConfigRadarOab {
  tenantId: string;
  nomeEscritorio: string;
  inscricoesOab: string[];
  webhookUrl?: string | null;
  telegramBotToken?: string | null;
  telegramChatId?: string | null;
  ativo: boolean;
  ultimaSincronizacaoUtc?: string | null;
}

export interface ItemRadarDjen {
  id: string;
  tenantId: string;
  oabOrigem: string;
  numeroProcesso: string;
  tribunal: string;
  orgaoJulgador: string;
  dataDisponibilizacao: string;
  tipoComunicacao: string;
  teorResumo: string;
  linkInteiroTeor: string;
  lida: boolean;
  convertidaEmTarefa: boolean;
  capturadoEmUtc: string;
}

export interface ItemWatchlistTenant {
  tenantId: string;
  numeroProcesso: string;
  classe: string;
  orgaoJulgador: string;
  tribunal: string;
  totalMovimentacoesConhecidas: number;
  adicionadoEmUtc: string;
  temNovidade: boolean;
}

export interface TeseJuridicaTenant {
  id: string;
  tenantId: string;
  titulo: string;
  areaDireito: string;
  eventosGatilho: string[];
  fundamentacaoLegal: string;
  precedenteChave: string;
  modeloArgumentacao: string;
  totalUsos: number;
  ultimoProcessoAplicado?: string | null;
  atualizadoEmUtc: string;
}

export interface AplicacaoTeseProcesso {
  id: string;
  tenantId: string;
  teseId: string;
  tituloTese: string;
  numeroProcesso: string;
  eventoContexto: string;
  resultadoEsperadoOuObtido: string;
  responsavel: string;
  aplicadaEmUtc: string;
}

export interface RecomendacaoTeseResult {
  tese: TeseJuridicaTenant;
  scoreAderencia: number;
  nivelAderencia: string;
  motivoEnquadramento: string;
  termosDetectadosNoProcesso: string[];
  jaUtilizadaNesteProcesso: boolean;
}

export interface SocioEmpresaQsa {
  nome: string;
  qualificacao: string;
  faixaEtaria: string;
}

export interface PublicacaoDiarioMunicipal {
  municipio: string;
  uf: string;
  dataPublicacao: string;
  excertoTexto: string;
  urlDiario: string;
}

export interface FichaPatrimonialParte {
  termoConsultado: string;
  cnpjFormatado?: string | null;
  razaoSocial: string;
  nomeFantasia: string;
  situacaoCadastral: string;
  capitalSocial: number;
  porteEmpresa: string;
  naturezaJuridica: string;
  atividadePrincipalCnae: string;
  enderecoDiligenciaCitacao: string;
  municipioUf: string;
  quadroSocietario: SocioEmpresaQsa[];
  mencoesDiariosMunicipais: PublicacaoDiarioMunicipal[];
  fonteDados: string;
  consultadoEmUtc: string;
}

export interface EstadoWorkspaceTenant {
  tenantId: string;
  configuracao: ConfigRadarOab;
  watchlist: ItemWatchlistTenant[];
  notasJuizo: Record<string, string>;
  suspensoesLocais: SuspensaoForenseLocal[];
  tarefasKanban: TarefaProcessual[];
  itensRadar: ItemRadarDjen[];
  tesesJuridicas?: TeseJuridicaTenant[];
  historicoAplicacoesTeses?: AplicacaoTeseProcesso[];
}

export interface ResultadoDisparoAlerta {
  sucesso: boolean;
  canal: string;
  statusCode: number;
  mensagemDiagnostico: string;
}

