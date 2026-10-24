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
}

export interface AdvogadoCarteira {
  numeroOab: string;
  nomeAdvogado?: string;
  tribunal: string;
  totalProcessos: number;
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
  dataPublicacaoDje: string;
  dataInicioPrazo: string;
  dataTermoFatal: string;
  diasUteisTotal: number;
  diasUteisRestantes: number;
  precluso: boolean;
  baseLegal: string;
  tipoAto: string;
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
