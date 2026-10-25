import type { TermoFatalCalculado } from '../models/processo.model';

export interface FeriadoConsiderado {
  data: string;
  descricao: string;
}

/**
 * Extrai a data civil (ano, mês, dia) ao meio-dia local, imune ao deslocamento de fuso horário
 * quando strings ISO em UTC próximas da meia-noite (ex: "2023-01-02T00:54:32Z") são lidas em UTC-3.
 */
export function extrairDataCivilSegura(entrada: string | Date): Date {
  if (entrada instanceof Date) {
    return new Date(entrada.getFullYear(), entrada.getMonth(), entrada.getDate(), 12, 0, 0);
  }

  const limpo = (entrada || '').trim();
  const matchIso = limpo.match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (matchIso) {
    const ano = Number(matchIso[1]);
    const mes = Number(matchIso[2]) - 1;
    const dia = Number(matchIso[3]);
    return new Date(ano, mes, dia, 12, 0, 0);
  }

  const matchBr = limpo.match(/^(\d{2})\/(\d{2})\/(\d{4})/);
  if (matchBr) {
    const dia = Number(matchBr[1]);
    const mes = Number(matchBr[2]) - 1;
    const ano = Number(matchBr[3]);
    return new Date(ano, mes, dia, 12, 0, 0);
  }

  const d = new Date(limpo);
  return new Date(d.getFullYear(), d.getMonth(), d.getDate(), 12, 0, 0);
}

/**
 * Algoritmo de Meeus/Jones/Butcher para cálculo exato do Domingo de Páscoa no calendário gregoriano.
 */
export function calcularDomingoDePascoa(ano: number): Date {
  const a = ano % 19;
  const b = Math.floor(ano / 100);
  const c = ano % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const mes = Math.floor((h + l - 7 * m + 114) / 31) - 1;
  const dia = ((h + l - 7 * m + 114) % 31) + 1;
  return new Date(ano, mes, dia, 12, 0, 0);
}

function mesmaDataCivil(a: Date, b: Date): boolean {
  return (
    a.getFullYear() === b.getFullYear() &&
    a.getMonth() === b.getMonth() &&
    a.getDate() === b.getDate()
  );
}

function adicionarDiasCivis(base: Date, dias: number): Date {
  return new Date(base.getFullYear(), base.getMonth(), base.getDate() + dias, 12, 0, 0);
}

export function formatarDataBr(d: Date): string {
  const dia = String(d.getDate()).padStart(2, '0');
  const mes = String(d.getMonth() + 1).padStart(2, '0');
  const ano = d.getFullYear();
  return `${dia}/${mes}/${ano}`;
}

/**
 * Verifica se uma data é dia útil forense segundo os arts. 219 e 220 do CPC/2015,
 * Lei nº 14.759/2023 (20/novembro), Lei nº 5.010/1966 e feriados móveis/estaduais/suspensões locais.
 */
export function ehDiaUtilForense(
  data: Date,
  siglaTribunal = 'TJSP',
  mapaSuspensoesLocais?: Map<string, string>
): { util: boolean; motivo?: string } {
  const diaSemana = data.getDay();
  if (diaSemana === 6) {
    return { util: false, motivo: 'Sábado (Art. 219 CPC)' };
  }
  if (diaSemana === 0) {
    return { util: false, motivo: 'Domingo (Art. 219 CPC)' };
  }

  const dataStrBr = formatarDataBr(data);
  if (mapaSuspensoesLocais && mapaSuspensoesLocais.has(dataStrBr)) {
    return { util: false, motivo: mapaSuspensoesLocais.get(dataStrBr) };
  }

  const mes = data.getMonth() + 1;
  const dia = data.getDate();

  // 1. Suspensão de Prazos / Recesso Forense (Art. 220 CPC: 20 de dezembro a 20 de janeiro)
  if ((mes === 12 && dia >= 20) || (mes === 1 && dia <= 20)) {
    return {
      util: false,
      motivo: 'Suspensão de Prazos / Recesso Forense (Art. 220 CPC - 20/dez a 20/jan)'
    };
  }

  // 2. Feriados Nacionais Fixos + Feriados Forenses Nacionais
  const chaveMesDia = `${String(mes).padStart(2, '0')}-${String(dia).padStart(2, '0')}`;
  const feriadosFixos: Record<string, string> = {
    '01-01': 'Confraternização Universal (Feriado Nacional)',
    '04-21': 'Tiradentes (Feriado Nacional)',
    '05-01': 'Dia do Trabalhador (Feriado Nacional)',
    '08-11': 'Dia do Advogado / Criação dos Cursos Jurídicos (Feriado Forense - Lei 5.010/66)',
    '09-07': 'Independência do Brasil (Feriado Nacional)',
    '10-12': 'Nossa Senhora Aparecida (Feriado Nacional)',
    '10-28': 'Dia do Servidor Público (Expediente Forense Suspenso - Art. 236 Lei 8.112/90)',
    '11-02': 'Finados (Feriado Nacional)',
    '11-15': 'Proclamação da República (Feriado Nacional)',
    '11-20': 'Dia Nacional de Zumbi e da Consciência Negra (Feriado Nacional - Lei 14.759/2023)',
    '12-08': 'Dia da Justiça (Feriado Forense - Decreto-Lei 8.292/45 e Lei 5.010/66)',
    '12-25': 'Natal (Feriado Nacional)'
  };

  if (feriadosFixos[chaveMesDia]) {
    return { util: false, motivo: feriadosFixos[chaveMesDia] };
  }

  // 3. Feriados Estaduais / Locais de São Paulo (TJSP, TRF3, TRT2, TRT15)
  const trib = (siglaTribunal || 'TJSP').toUpperCase();
  if (trib === 'TJSP' || trib === 'TRF3' || trib === 'TRT2' || trib === 'TRT15') {
    if (chaveMesDia === '01-25') {
      return { util: false, motivo: 'Aniversário da Cidade de São Paulo (Feriado Municipal SP)' };
    }
    if (chaveMesDia === '07-09') {
      return {
        util: false,
        motivo: 'Revolução Constitucionalista de 1932 (Feriado Estadual SP - Lei 9.497/97)'
      };
    }
  }

  // 4. Feriados Móveis calculados a partir da Páscoa
  const pascoa = calcularDomingoDePascoa(data.getFullYear());
  const segundaCarnaval = adicionarDiasCivis(pascoa, -48);
  const tercaCarnaval = adicionarDiasCivis(pascoa, -47);
  const quintaSanta = adicionarDiasCivis(pascoa, -3);
  const sextaSanta = adicionarDiasCivis(pascoa, -2);
  const corpusChristi = adicionarDiasCivis(pascoa, 60);

  if (mesmaDataCivil(data, segundaCarnaval) || mesmaDataCivil(data, tercaCarnaval)) {
    return { util: false, motivo: 'Carnaval (Expediente Forense Suspenso)' };
  }
  if (mesmaDataCivil(data, quintaSanta)) {
    return {
      util: false,
      motivo: 'Quinta-feira Santa / Endoenças (Feriado Forense - Lei 5.010/66, art. 62, II)'
    };
  }
  if (mesmaDataCivil(data, sextaSanta)) {
    return { util: false, motivo: 'Sexta-feira Santa / Paixão de Cristo (Feriado Nacional)' };
  }
  if (mesmaDataCivil(data, corpusChristi)) {
    return { util: false, motivo: 'Corpus Christi (Feriado Forense)' };
  }

  return { util: true };
}

function construirMapaSuspensoes(
  suspensoes?: FeriadoConsiderado[]
): Map<string, string> | undefined {
  if (!suspensoes || suspensoes.length === 0) return undefined;
  const mapa = new Map<string, string>();
  for (const s of suspensoes) {
    if (!s.data || !s.descricao) continue;
    const d = extrairDataCivilSegura(s.data);
    mapa.set(formatarDataBr(d), s.descricao.trim());
  }
  return mapa.size > 0 ? mapa : undefined;
}

function obterProximoDiaUtil(
  dataBase: Date,
  siglaTribunal: string,
  feriadosEncontrados: FeriadoConsiderado[],
  mapaSuspensoesLocais?: Map<string, string>
): Date {
  let cursor = adicionarDiasCivis(dataBase, 1);
  while (true) {
    const check = ehDiaUtilForense(cursor, siglaTribunal, mapaSuspensoesLocais);
    if (check.util) {
      return cursor;
    }
    const diaSemana = cursor.getDay();
    if (diaSemana !== 0 && diaSemana !== 6 && check.motivo) {
      const dataStr = formatarDataBr(cursor);
      if (!feriadosEncontrados.some((f) => f.data === dataStr)) {
        feriadosEncontrados.push({ data: dataStr, descricao: check.motivo });
      }
    }
    cursor = adicionarDiasCivis(cursor, 1);
  }
}

export function calcularDiasUteisRestantes(
  dataReferencia: Date,
  dataTermoFatal: Date,
  siglaTribunal = 'TJSP',
  mapaSuspensoesLocais?: Map<string, string>
): number {
  const ref = extrairDataCivilSegura(dataReferencia);
  const alvo = extrairDataCivilSegura(dataTermoFatal);

  if (ref.getTime() >= alvo.getTime()) return 0;

  let cursor = ref;
  let cont = 0;
  while (cursor.getTime() < alvo.getTime()) {
    cursor = adicionarDiasCivis(cursor, 1);
    if (ehDiaUtilForense(cursor, siglaTribunal, mapaSuspensoesLocais).util) {
      cont++;
    }
  }
  return cont;
}

export function calcularTermoFatalForense(params: {
  dataMarco: string | Date;
  diasPrazoBase: number;
  ehDisponibilizacaoDjen: boolean;
  prazoEmDobro: boolean;
  siglaTribunal: string;
  tipoAto?: string;
  dataReferenciaHoje?: Date;
  suspensoesLocais?: FeriadoConsiderado[];
}): TermoFatalCalculado {
  const base = params.diasPrazoBase > 0 ? params.diasPrazoBase : 15;
  const trib = (params.siglaTribunal || 'TJSP').toUpperCase();
  const diasUteisTotal = params.prazoEmDobro ? base * 2 : base;
  const feriadosConsiderados: FeriadoConsiderado[] = [];
  const mapaSuspensoes = construirMapaSuspensoes(params.suspensoesLocais);

  const dataDisp = extrairDataCivilSegura(params.dataMarco);
  let dataPublicacao: Date;

  if (params.ehDisponibilizacaoDjen) {
    // Art. 224, § 2º CPC e Res. CNJ 455/2022 art. 11, § 3º:
    // Publicação considera-se no 1º dia útil seguinte ao da disponibilização no DJEN.
    dataPublicacao = obterProximoDiaUtil(dataDisp, trib, feriadosConsiderados, mapaSuspensoes);
  } else {
    const checkMarco = ehDiaUtilForense(dataDisp, trib, mapaSuspensoes);
    dataPublicacao = checkMarco.util
      ? dataDisp
      : obterProximoDiaUtil(dataDisp, trib, feriadosConsiderados, mapaSuspensoes);
  }

  // Art. 224, § 3º CPC: Início do prazo no 1º dia útil seguinte à data da publicação.
  const dataInicio = obterProximoDiaUtil(dataPublicacao, trib, feriadosConsiderados, mapaSuspensoes);

  let dataTermoFatal = dataInicio;
  let contados = 1;

  while (contados < diasUteisTotal) {
    dataTermoFatal = adicionarDiasCivis(dataTermoFatal, 1);
    const check = ehDiaUtilForense(dataTermoFatal, trib, mapaSuspensoes);
    if (check.util) {
      contados++;
    } else {
      const diaSemana = dataTermoFatal.getDay();
      if (diaSemana !== 0 && diaSemana !== 6 && check.motivo) {
        const dataStr = formatarDataBr(dataTermoFatal);
        if (!feriadosConsiderados.some((f) => f.data === dataStr)) {
          feriadosConsiderados.push({ data: dataStr, descricao: check.motivo });
        }
      }
    }
  }

  const hoje = params.dataReferenciaHoje
    ? extrairDataCivilSegura(params.dataReferenciaHoje)
    : extrairDataCivilSegura(new Date());

  const diasRestantes = calcularDiasUteisRestantes(hoje, dataTermoFatal, trib, mapaSuspensoes);

  const ritoTexto =
    base === 5
      ? 'Art. 1.023 CPC (Embargos de Declaração - 5 dias úteis)'
      : `Art. 1.003, § 5º c/c Art. 219 CPC (${base} dias úteis)`;
  const djenTexto = params.ehDisponibilizacaoDjen
    ? ' + Art. 224, §§ 2º e 3º CPC / Res. CNJ 455/2022 (DJEN)'
    : ' + Art. 224, § 3º CPC';
  const dobroTexto = params.prazoEmDobro ? ' + Prazo em Dobro (Art. 183, 180 e 186 do CPC)' : '';

  return {
    dataDisponibilizacaoDjen: formatarDataBr(dataDisp),
    dataPublicacaoDje: formatarDataBr(dataPublicacao),
    dataInicioPrazo: formatarDataBr(dataInicio),
    dataTermoFatal: formatarDataBr(dataTermoFatal),
    diasPrazoBase: base,
    prazoEmDobro: params.prazoEmDobro,
    ehDisponibilizacaoDjen: params.ehDisponibilizacaoDjen,
    diasUteisTotal,
    diasUteisRestantes: diasRestantes,
    precluso: hoje.getTime() > dataTermoFatal.getTime(),
    baseLegal: ritoTexto + djenTexto + dobroTexto,
    tipoAto: params.tipoAto || (base === 5 ? 'Embargos de Declaração' : 'Recurso / Manifestação'),
    feriadosConsiderados
  };
}

function escaparTextoIcs(valor: string): string {
  return (valor || '')
    .replace(/\\/g, '\\\\')
    .replace(/;/g, '\\;')
    .replace(/,/g, '\\,')
    .replace(/\r?\n/g, '\\n');
}

function formatarDataYyyyMmDd(d: Date): string {
  const ano = d.getFullYear();
  const mes = String(d.getMonth() + 1).padStart(2, '0');
  const dia = String(d.getDate()).padStart(2, '0');
  return `${ano}${mes}${dia}`;
}

/**
 * Gera conteúdo de arquivo RFC 5545 (.ics) com alarmes proativos em D-2 e D-1.
 */
export function gerarConteudoIcsPrazoFatal(params: {
  numeroProcesso: string;
  classeProcessual: string;
  orgaoJulgador: string;
  siglaTribunal: string;
  calculo: TermoFatalCalculado;
  responsavel?: string;
}): string {
  const dataFatal = extrairDataCivilSegura(params.calculo.dataTermoFatal);
  const dataFim = adicionarDiasCivis(dataFatal, 1);
  const dtStart = formatarDataYyyyMmDd(dataFatal);
  const dtEnd = formatarDataYyyyMmDd(dataFim);
  const cnj = (params.numeroProcesso || 'Processo').trim();
  const classe = (params.classeProcessual || 'Prazo Processual').trim();
  const vara = (params.orgaoJulgador || params.siglaTribunal || 'TJSP').trim();
  const trib = (params.siglaTribunal || 'TJSP').trim();
  const resp = (params.responsavel || 'Advogado Responsável').trim();

  const linhasDesc = [
    `VENCIMENTO FATAL DE PRAZO PROCESSUAL (${params.calculo.diasUteisTotal} dias úteis)`,
    `Processo CNJ: ${cnj}`,
    `Classe: ${classe}`,
    `Órgão Julgador: ${vara} (${trib})`,
    `Ato/Rito: ${params.calculo.tipoAto}`,
    `Responsável: ${resp}`,
    '',
    'MEMÓRIA DE CÁLCULO (CPC/2015):',
    `- Disponibilização/Marco: ${params.calculo.dataDisponibilizacaoDjen}`,
    `- Publicação Oficial (Art. 224 §2º): ${params.calculo.dataPublicacaoDje}`,
    `- Início da Contagem (Art. 224 §3º): ${params.calculo.dataInicioPrazo}`,
    `- Termo Final Fatal: ${params.calculo.dataTermoFatal}`,
    `- Base Legal: ${params.calculo.baseLegal}`
  ];

  if (params.calculo.feriadosConsiderados.length > 0) {
    linhasDesc.push('', 'Feriados / Suspensões Ignorados:');
    for (const f of params.calculo.feriadosConsiderados) {
      linhasDesc.push(`- ${f.data}: ${f.descricao}`);
    }
  }

  const uid = `prazo-${cnj.replace(/\D/g, '')}-${dtStart}@legalprocessmonitor.jus`;
  const stamp = new Date().toISOString().replace(/[-:]/g, '').split('.')[0] + 'Z';

  return [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//LegalProcessMonitor//Calculadora Forense CPC 2015//PT-BR',
    'CALSCALE:GREGORIAN',
    'METHOD:PUBLISH',
    'BEGIN:VEVENT',
    `UID:${uid}`,
    `DTSTAMP:${stamp}`,
    `DTSTART;VALUE=DATE:${dtStart}`,
    `DTEND;VALUE=DATE:${dtEnd}`,
    `SUMMARY:[PRAZO FATAL] ${escaparTextoIcs(cnj)} - ${escaparTextoIcs(classe)} (${params.calculo.diasUteisTotal}d úteis)`,
    `LOCATION:${escaparTextoIcs(vara)} - ${escaparTextoIcs(trib)}`,
    `DESCRIPTION:${escaparTextoIcs(linhasDesc.join('\n'))}`,
    'STATUS:CONFIRMED',
    'TRANSP:OPAQUE',
    'BEGIN:VALARM',
    'TRIGGER:-P2D',
    'ACTION:DISPLAY',
    `DESCRIPTION:Alerta D-2: Prazo fatal do processo ${escaparTextoIcs(cnj)} vence em 2 dias!`,
    'END:VALARM',
    'BEGIN:VALARM',
    'TRIGGER:-P1D',
    'ACTION:DISPLAY',
    `DESCRIPTION:URGENTE D-1: Prazo fatal do processo ${escaparTextoIcs(cnj)} vence AMANHÃ!`,
    'END:VALARM',
    'END:VEVENT',
    'END:VCALENDAR',
    ''
  ].join('\r\n');
}

/**
 * Gera deep-link direto para adicionar o prazo fatal ao Google Calendar em 1 clique.
 */
export function gerarLinkGoogleCalendarPrazoFatal(params: {
  numeroProcesso: string;
  classeProcessual: string;
  orgaoJulgador: string;
  siglaTribunal: string;
  calculo: TermoFatalCalculado;
}): string {
  const dataFatal = extrairDataCivilSegura(params.calculo.dataTermoFatal);
  const dataFim = adicionarDiasCivis(dataFatal, 1);
  const dates = `${formatarDataYyyyMmDd(dataFatal)}/${formatarDataYyyyMmDd(dataFim)}`;
  const text = `[PRAZO FATAL] ${params.numeroProcesso} - ${params.calculo.tipoAto} (${params.calculo.diasUteisTotal}d úteis)`;
  const details = [
    `Processo CNJ: ${params.numeroProcesso}`,
    `Classe: ${params.classeProcessual}`,
    `Órgão Julgador: ${params.orgaoJulgador} (${params.siglaTribunal})`,
    `Disponibilização DJEN: ${params.calculo.dataDisponibilizacaoDjen}`,
    `Publicação Oficial: ${params.calculo.dataPublicacaoDje}`,
    `Início do Prazo: ${params.calculo.dataInicioPrazo}`,
    `Vencimento Fatal: ${params.calculo.dataTermoFatal}`,
    `Base Legal: ${params.calculo.baseLegal}`
  ].join('\n');
  const location = `${params.orgaoJulgador} - ${params.siglaTribunal}`;

  const query = new URLSearchParams({
    action: 'TEMPLATE',
    text,
    dates,
    details,
    location
  });

  return `https://calendar.google.com/calendar/render?${query.toString()}`;
}

