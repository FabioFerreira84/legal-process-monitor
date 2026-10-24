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
  movimentacoes: Movimentacao[];
  envolvidos: Envolvido[];
  documentosPublicos?: DocumentoProcessual[];
}
