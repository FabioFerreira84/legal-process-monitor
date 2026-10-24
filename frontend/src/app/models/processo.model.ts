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
