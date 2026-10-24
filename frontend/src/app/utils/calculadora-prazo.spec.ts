import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  ehDiaUtilForense,
  extrairDataCivilSegura,
  calcularTermoFatalForense
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
});
