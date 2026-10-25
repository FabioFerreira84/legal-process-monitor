import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  ehDiaUtilForense,
  extrairDataCivilSegura,
  calcularTermoFatalForense,
  gerarConteudoIcsPrazoFatal,
  gerarLinkGoogleCalendarPrazoFatal,
  calcularDigitoVerificadorCnj
} from './calculadora-prazo.ts';

describe('Calculadora de Prazo Forense (CPC/2015 + Lei 14.759/2023 + Res. CNJ 455/2022)', () => {
  it('não deve deslocar o dia civil em timestamps UTC próximos da meia-noite', () => {
    const d = extrairDataCivilSegura('2023-01-02T00:54:32Z');
    assert.equal(d.getFullYear(), 2023);
    assert.equal(d.getMonth() + 1, 1);
    assert.equal(d.getDate(), 2);
  });

  it('deve reconhecer 20 de novembro como feriado nacional (Lei 14.759/2023)', () => {
    const vinteNov = new Date(2026, 10, 20, 12, 0, 0); // 20/11/2026 (sexta-feira)
    const res = ehDiaUtilForense(vinteNov, 'TJSP');
    assert.equal(res.util, false);
    assert.match(res.motivo || '', /Consciência Negra/);
  });

  it('deve reconhecer Carnaval, Sexta-Feira Santa, Corpus Christi e Recesso Art. 220 CPC', () => {
    assert.equal(ehDiaUtilForense(new Date(2026, 1, 17, 12, 0, 0), 'TJSP').util, false); // Terça de Carnaval 2026
    assert.equal(ehDiaUtilForense(new Date(2026, 3, 3, 12, 0, 0), 'TJSP').util, false);  // Sexta-Feira Santa 2026
    assert.equal(ehDiaUtilForense(new Date(2026, 5, 4, 12, 0, 0), 'TJSP').util, false);  // Corpus Christi 2026
    assert.equal(ehDiaUtilForense(new Date(2025, 11, 20, 12, 0, 0), 'TJSP').util, false); // 20/12 (Recesso)
    assert.equal(ehDiaUtilForense(new Date(2026, 0, 20, 12, 0, 0), 'TJSP').util, false);  // 20/01 (Recesso)
  });

  it('deve calcular corretamente disponibilização DJEN na sexta-feira 23/10/2026 para 15 dias úteis', () => {
    const res = calcularTermoFatalForense({
      dataMarco: '2026-10-23',
      diasPrazoBase: 15,
      ehDisponibilizacaoDjen: true,
      prazoEmDobro: false,
      siglaTribunal: 'TJSP',
      tipoAto: 'Recurso / Contrarrazões',
      dataReferenciaHoje: new Date(2026, 9, 24, 12, 0, 0) // 24/10/2026 (sábado)
    });

    assert.equal(res.dataDisponibilizacaoDjen, '23/10/2026');
    assert.equal(res.dataPublicacaoDje, '26/10/2026');
    assert.equal(res.dataInicioPrazo, '27/10/2026');
    assert.equal(res.dataTermoFatal, '18/11/2026');
    assert.equal(res.diasUteisRestantes, 16);
    assert.equal(res.feriadosConsiderados.length >= 2, true); // 28/10 e 02/11
  });

  it('deve manter consistência entre termo fatal e dias restantes atravessando o recesso forense (Art. 220 CPC)', () => {
    const res = calcularTermoFatalForense({
      dataMarco: '2025-12-17',
      diasPrazoBase: 5,
      ehDisponibilizacaoDjen: true,
      prazoEmDobro: false,
      siglaTribunal: 'TJSP',
      tipoAto: 'Embargos de Declaração',
      dataReferenciaHoje: new Date(2025, 11, 22, 12, 0, 0) // Durante o recesso
    });

    assert.equal(res.dataPublicacaoDje, '18/12/2025');
    assert.equal(res.dataInicioPrazo, '19/12/2025');
    assert.equal(res.dataTermoFatal, '26/01/2026');
    assert.equal(res.diasUteisRestantes, 4);
  });

  it('deve dobrar o prazo quando prazoEmDobro=true (Art. 183 CPC)', () => {
    const res = calcularTermoFatalForense({
      dataMarco: '2026-10-23',
      diasPrazoBase: 15,
      ehDisponibilizacaoDjen: true,
      prazoEmDobro: true,
      siglaTribunal: 'TRF3'
    });

    assert.equal(res.diasUteisTotal, 30);
    assert.match(res.baseLegal, /Art\. 183/);
  });

  it('deve pular suspensões locais customizadas pelo escritório e registrá-las na memória de cálculo', () => {
    const res = calcularTermoFatalForense({
      dataMarco: '2026-10-23',
      diasPrazoBase: 15,
      ehDisponibilizacaoDjen: true,
      prazoEmDobro: false,
      siglaTribunal: 'TJSP',
      dataReferenciaHoje: new Date(2026, 9, 24, 12, 0, 0),
      suspensoesLocais: [
        { data: '2026-10-29', descricao: 'Provimento CSM - Indisponibilidade do Sistema' }
      ]
    });

    assert.equal(res.dataTermoFatal, '19/11/2026');
    assert.equal(
      res.feriadosConsiderados.some((f) => f.data === '29/10/2026' && f.descricao.includes('Provimento CSM')),
      true
    );
  });

  it('deve gerar conteúdo .ICS RFC 5545 válido com VALARM D-2 e D-1 e link Google Calendar', () => {
    const calculo = calcularTermoFatalForense({
      dataMarco: '2026-10-23',
      diasPrazoBase: 15,
      ehDisponibilizacaoDjen: true,
      prazoEmDobro: false,
      siglaTribunal: 'TJSP',
      dataReferenciaHoje: new Date(2026, 9, 24, 12, 0, 0)
    });

    const ics = gerarConteudoIcsPrazoFatal({
      numeroProcesso: '2101760-84.2025.8.26.0000',
      classeProcessual: 'Agravo de Instrumento',
      orgaoJulgador: '24ª Câmara de Direito Privado',
      siglaTribunal: 'TJSP',
      calculo
    });

    assert.match(ics, /^BEGIN:VCALENDAR/);
    assert.match(ics, /DTSTART;VALUE=DATE:20261118/);
    assert.match(ics, /TRIGGER:-P2D/);
    assert.match(ics, /TRIGGER:-P1D/);

    const gcal = gerarLinkGoogleCalendarPrazoFatal({
      numeroProcesso: '2101760-84.2025.8.26.0000',
      classeProcessual: 'Agravo de Instrumento',
      orgaoJulgador: '24ª Câmara de Direito Privado',
      siglaTribunal: 'TJSP',
      calculo
    });

    assert.match(gcal, /calendar\.google\.com\/calendar\/render\?action=TEMPLATE/);
    assert.match(gcal, /dates=20261118%2F20261119/);
  });

  it('deve validar corretamente o dígito verificador Módulo 97 do CNJ (98 - resto) nos casos paradigmáticos reais', () => {
    const tjsp = calcularDigitoVerificadorCnj('2101760-84.2025.8.26.0000');
    assert.equal(tjsp.valido, true);
    assert.equal(tjsp.dvEsperado, '84');

    const trf3 = calcularDigitoVerificadorCnj('5000001-05.2023.4.03.6100');
    assert.equal(trf3.valido, true);
    assert.equal(trf3.dvEsperado, '05');

    const guarulhos = calcularDigitoVerificadorCnj('1006805-85.2025.8.26.0224');
    assert.equal(guarulhos.valido, true);
    assert.equal(guarulhos.dvEsperado, '85');
  });
});
