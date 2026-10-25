import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Observable, of } from 'rxjs';
import { switchMap, map, catchError } from 'rxjs/operators';
import {
  Processo,
  AdvogadoCarteira,
  ProcessoResumo,
  PerfilMagistrado,
  EstadoWorkspaceTenant,
  ConfigRadarOab,
  ItemWatchlistTenant,
  SuspensaoForenseLocal,
  TarefaProcessual,
  StatusTarefaColuna,
  PrioridadeTarefa,
  ItemRadarDjen,
  ResultadoDisparoAlerta,
  TeseJuridicaTenant,
  AplicacaoTeseProcesso,
  RecomendacaoTeseResult,
  FichaPatrimonialParte,
  MinutaProcessualGerada,
  ResultadoCalculoFinanceiroProcesso
} from '../models/processo.model';

@Injectable({
  providedIn: 'root'
})
export class ProcessoService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl =
    typeof window !== 'undefined' && window.location.hostname === 'localhost'
      ? 'http://localhost:5000/api'
      : 'https://legal-process-monitor-api.onrender.com/api';

  private headersTenant(tenantId: string): { headers: HttpHeaders } {
    return {
      headers: new HttpHeaders({
        'X-Tenant-Id': (tenantId || 'escritorio-ferreira').trim().toLowerCase()
      })
    };
  }

  consultarProcesso(numeroProcesso: string, forcarAtualizacao = false): Observable<Processo> {
    const url = `${this.baseUrl}/processos/${encodeURIComponent(numeroProcesso)}?forcarAtualizacao=${forcarAtualizacao}`;
    return this.http.get<Processo>(url);
  }

  consultarCarteiraOab(numeroOab: string): Observable<AdvogadoCarteira> {
    const url = `${this.baseUrl}/advogados/${encodeURIComponent(numeroOab)}`;
    return this.http.get<AdvogadoCarteira>(url).pipe(
      switchMap(carteira => {
        // Se a API backend encontrou processos normalmente, retorna direto
        if (carteira && carteira.processos && carteira.processos.length > 0) {
          return of(carteira);
        }
        // Se o backend retornou 0 processos ou bloqueio de WAF/CloudFront (servidores fora do BR como Render), consulta direto no DJEN com o IP do cliente
        return this.consultarDjenDiretoNavegador(numeroOab, carteira);
      }),
      catchError(() => this.consultarDjenDiretoNavegador(numeroOab))
    );
  }

  private consultarDjenDiretoNavegador(numeroOab: string, carteiraBase?: AdvogadoCarteira): Observable<AdvogadoCarteira> {
    const oabLimpa = (numeroOab || '').replace(/[^a-zA-Z0-9]/g, '').toUpperCase();
    const numDigitos = oabLimpa.replace(/\D/g, '');
    let uf = oabLimpa.replace(/[^A-Z]/g, '');
    if (!uf) uf = 'SP';

    if (!numDigitos) {
      return of(carteiraBase || {
        numeroOab,
        tribunal: `DJEN/CNJ Nacional (${uf})`,
        totalProcessos: 0,
        totalComunicacoesDjen: 0,
        dataConsulta: new Date().toISOString(),
        processos: [],
        avisosOuErros: ['Número de OAB inválido.']
      });
    }

    const djenUrl = `https://comunicaapi.pje.jus.br/api/v1/comunicacao?numeroOab=${encodeURIComponent(numDigitos)}&ufOab=${encodeURIComponent(uf)}&itensPorPagina=100&pagina=1`;

    return this.http.get<any>(djenUrl).pipe(
      map(res => {
        const carteira: AdvogadoCarteira = carteiraBase ? { ...carteiraBase } : {
          numeroOab: `${numDigitos}${uf}`,
          tribunal: uf === 'RJ' ? 'TJRJ + TRF2 + TRT1 (Ecossistema Fluminense) + DJEN' : `DJEN/CNJ Nacional (${uf})`,
          totalProcessos: 0,
          totalComunicacoesDjen: 0,
          dataConsulta: new Date().toISOString(),
          processos: [],
          fontesConsultadas: [],
          avisosOuErros: []
        };

        carteira.totalComunicacoesDjen = res.count || 0;
        carteira.fontesConsultadas = carteira.fontesConsultadas || [];
        if (!carteira.fontesConsultadas.includes('DJEN/CNJ Oficial (comunicaapi.pje.jus.br)')) {
          carteira.fontesConsultadas.push('DJEN/CNJ Oficial (comunicaapi.pje.jus.br)');
        }

        const items: any[] = res.items || [];
        const processosMapeados = new Map<string, ProcessoResumo>();

        for (const item of items) {
          const cnjLimpo = (item.numero_processo || '').replace(/\D/g, '');
          const cnjMascara = item.numeroprocessocommascara || '';
          if (!cnjLimpo || cnjLimpo.length !== 20) continue;

          // Extrai o nome do advogado se ainda não tiver
          if (!carteira.nomeAdvogado && Array.isArray(item.destinatarioadvogados)) {
            for (const dest of item.destinatarioadvogados) {
              const adv = dest.advogado;
              if (adv && String(adv.numero_oab) === numDigitos && adv.nome) {
                carteira.nomeAdvogado = adv.nome.trim();
                break;
              }
            }
          }

          if (processosMapeados.has(cnjLimpo)) continue;

          const siglaTribunal = item.siglaTribunal || 'TJSP';
          const nomeClasse = item.nomeClasse || 'Processo Judicial';
          const tipoDoc = item.tipoDocumento || '';
          const nomeOrgao = item.nomeOrgao || '';
          const dataDisp = item.datadisponibilizacao || '';
          const link = item.link || '';
          const texto = (item.texto || '').replace(/<[^>]+>/g, ' ').replace(/\s+/g, ' ').trim();

          const grau = ['STJ', 'STF', 'TST'].includes(siglaTribunal) ? 3 :
            (cnjLimpo.endsWith('0000') || /câmara|turma/i.test(nomeOrgao) ? 2 : 1);

          processosMapeados.set(cnjLimpo, {
            numeroCnjFormatado: cnjMascara || cnjLimpo,
            numeroCnjLimpo: cnjLimpo,
            tribunal: siglaTribunal,
            grau,
            classe: tipoDoc ? `${nomeClasse} (${tipoDoc})` : nomeClasse,
            vara: nomeOrgao,
            papel: `Intimado via DJEN (OAB ${numDigitos}/${uf})`,
            dataRecebimento: dataDisp,
            urlOrigem: link || 'https://comunica.pje.jus.br',
            fonteDados: `DJEN/CNJ (${siglaTribunal})`,
            ultimaComunicacaoResumo: texto.length > 250 ? texto.substring(0, 250) + '...' : texto
          });
        }

        const novosProcessos = Array.from(processosMapeados.values());
        carteira.processos = [...(carteira.processos || []), ...novosProcessos];
        carteira.totalProcessos = carteira.processos.length;
        // Limpa aviso de 403 se os processos foram recuperados com sucesso via navegador do cliente
        carteira.avisosOuErros = (carteira.avisosOuErros || []).filter(e => !e.includes('403'));

        return carteira;
      }),
      catchError(err => {
        return of(carteiraBase || {
          numeroOab,
          tribunal: `DJEN/CNJ Nacional (${uf})`,
          totalProcessos: 0,
          totalComunicacoesDjen: 0,
          dataConsulta: new Date().toISOString(),
          processos: [],
          avisosOuErros: [`Erro ao consultar DJEN/CNJ: ${err.message || 'Falha de conexão'}`]
        });
      })
    );
  }

  obterMagistradoPorNome(nome: string, tribunal = 'TJSP'): Observable<PerfilMagistrado> {
    const url = `${this.baseUrl}/magistrados/busca?nome=${encodeURIComponent(nome)}&tribunal=${encodeURIComponent(tribunal)}`;
    return this.http.get<PerfilMagistrado>(url);
  }

  listarMagistrados(): Observable<PerfilMagistrado[]> {
    return this.http.get<PerfilMagistrado[]>(`${this.baseUrl}/magistrados`);
  }

  ping(): Observable<{ status: string; timestamp: string }> {
    return this.http.get<{ status: string; timestamp: string }>(`${this.baseUrl}/processos/ping`);
  }

  listarRecentes(limite = 20): Observable<Processo[]> {
    return this.http.get<Processo[]>(`${this.baseUrl}/processos?limite=${limite}`);
  }

  // ============================================================================
  // ENDPOINTS MULTI-TENANT SaaS (/api/workspaces)
  // ============================================================================
  obterEstadoWorkspace(tenantId: string): Observable<EstadoWorkspaceTenant> {
    return this.http.get<EstadoWorkspaceTenant>(
      `${this.baseUrl}/workspaces`,
      this.headersTenant(tenantId)
    );
  }

  atualizarConfigWorkspace(
    tenantId: string,
    payload: {
      nomeEscritorio: string;
      inscricoesOab: string[];
      webhookUrl?: string | null;
      telegramBotToken?: string | null;
      telegramChatId?: string | null;
      ativo: boolean;
    }
  ): Observable<ConfigRadarOab> {
    return this.http.put<ConfigRadarOab>(
      `${this.baseUrl}/workspaces/config`,
      payload,
      this.headersTenant(tenantId)
    );
  }

  salvarWatchlistTenant(
    tenantId: string,
    payload: {
      numeroProcesso: string;
      classe: string;
      orgaoJulgador: string;
      tribunal: string;
      totalMovimentacoesConhecidas: number;
      temNovidade?: boolean;
    }
  ): Observable<ItemWatchlistTenant> {
    return this.http.post<ItemWatchlistTenant>(
      `${this.baseUrl}/workspaces/watchlist`,
      payload,
      this.headersTenant(tenantId)
    );
  }

  removerWatchlistTenant(tenantId: string, numeroProcesso: string): Observable<{ removido: boolean }> {
    return this.http.delete<{ removido: boolean }>(
      `${this.baseUrl}/workspaces/watchlist/${encodeURIComponent(numeroProcesso)}`,
      this.headersTenant(tenantId)
    );
  }

  salvarNotaJuizoTenant(
    tenantId: string,
    chaveUnidade: string,
    conteudoNota: string
  ): Observable<{ chaveUnidade: string; conteudoNota: string }> {
    return this.http.put<{ chaveUnidade: string; conteudoNota: string }>(
      `${this.baseUrl}/workspaces/notas-juizo`,
      { chaveUnidade, conteudoNota },
      this.headersTenant(tenantId)
    );
  }

  adicionarSuspensaoLocal(
    tenantId: string,
    payload: { dataIso: string; descricao: string; comarcaOuTribunal: string }
  ): Observable<SuspensaoForenseLocal> {
    return this.http.post<SuspensaoForenseLocal>(
      `${this.baseUrl}/workspaces/suspensoes`,
      payload,
      this.headersTenant(tenantId)
    );
  }

  removerSuspensaoLocal(tenantId: string, id: string): Observable<{ removido: boolean }> {
    return this.http.delete<{ removido: boolean }>(
      `${this.baseUrl}/workspaces/suspensoes/${encodeURIComponent(id)}`,
      this.headersTenant(tenantId)
    );
  }

  salvarTarefaKanban(
    tenantId: string,
    payload: {
      id?: string;
      numeroProcesso: string;
      titulo: string;
      descricao?: string;
      responsavel?: string;
      coluna: StatusTarefaColuna;
      prioridade: PrioridadeTarefa;
      prazoFatalIso: string;
      diasPrazo: number;
      prazoEmDobro: boolean;
      numeroProtocolo?: string | null;
    }
  ): Observable<TarefaProcessual> {
    return this.http.post<TarefaProcessual>(
      `${this.baseUrl}/workspaces/tarefas`,
      payload,
      this.headersTenant(tenantId)
    );
  }

  removerTarefaKanban(tenantId: string, id: string): Observable<{ removido: boolean }> {
    return this.http.delete<{ removido: boolean }>(
      `${this.baseUrl}/workspaces/tarefas/${encodeURIComponent(id)}`,
      this.headersTenant(tenantId)
    );
  }

  sincronizarRadarDjen(
    tenantId: string,
    oabAdicional?: string,
    dispararAlerta = true
  ): Observable<{
    novasPublicacoesInseridas: number;
    totalNoRadar: number;
    configuracao: ConfigRadarOab;
    itensRadar: ItemRadarDjen[];
    alerta?: ResultadoDisparoAlerta;
  }> {
    return this.http.post<{
      novasPublicacoesInseridas: number;
      totalNoRadar: number;
      configuracao: ConfigRadarOab;
      itensRadar: ItemRadarDjen[];
      alerta?: ResultadoDisparoAlerta;
    }>(
      `${this.baseUrl}/workspaces/radar/sincronizar`,
      { oabAdicional, dispararAlerta },
      this.headersTenant(tenantId)
    );
  }

  converterRadarEmTarefa(
    tenantId: string,
    itemId: string,
    payload: { diasPrazoBase: number; prazoEmDobro: boolean; responsavel?: string }
  ): Observable<{ tarefa: TarefaProcessual }> {
    return this.http.post<{ tarefa: TarefaProcessual }>(
      `${this.baseUrl}/workspaces/radar/${encodeURIComponent(itemId)}/converter-tarefa`,
      payload,
      this.headersTenant(tenantId)
    );
  }

  // ============================================================================
  // BANCO DE TESES JURÍDICAS & RECOMENDAÇÃO + FICHA PATRIMONIAL + FERIADOS
  // ============================================================================
  salvarTeseJuridica(
    tenantId: string,
    payload: {
      id?: string;
      titulo: string;
      areaDireito: string;
      eventosGatilho: string[];
      fundamentacaoLegal: string;
      precedenteChave: string;
      modeloArgumentacao: string;
      totalUsos?: number;
      ultimoProcessoAplicado?: string | null;
    }
  ): Observable<TeseJuridicaTenant> {
    return this.http.post<TeseJuridicaTenant>(
      `${this.baseUrl}/workspaces/teses`,
      payload,
      this.headersTenant(tenantId)
    );
  }

  removerTeseJuridica(tenantId: string, teseId: string): Observable<{ removido: boolean }> {
    return this.http.delete<{ removido: boolean }>(
      `${this.baseUrl}/workspaces/teses/${encodeURIComponent(teseId)}`,
      this.headersTenant(tenantId)
    );
  }

  aplicarTeseNoProcesso(
    tenantId: string,
    payload: {
      teseId: string;
      numeroProcesso: string;
      eventoContexto?: string;
      resultadoEsperadoOuObtido?: string;
      responsavel?: string;
      criarTarefaKanban?: boolean;
      prazoFatalIso?: string;
    }
  ): Observable<{
    aplicacao: AplicacaoTeseProcesso;
    tarefaKanban?: TarefaProcessual;
    teses: TeseJuridicaTenant[];
  }> {
    return this.http.post<{
      aplicacao: AplicacaoTeseProcesso;
      tarefaKanban?: TarefaProcessual;
      teses: TeseJuridicaTenant[];
    }>(`${this.baseUrl}/workspaces/teses/aplicar`, payload, this.headersTenant(tenantId));
  }

  recomendarTesesParaProcesso(
    tenantId: string,
    numeroProcesso: string
  ): Observable<RecomendacaoTeseResult[]> {
    return this.http.get<RecomendacaoTeseResult[]>(
      `${this.baseUrl}/workspaces/teses/recomendar/${encodeURIComponent(numeroProcesso)}`,
      this.headersTenant(tenantId)
    );
  }

  consultarFichaPatrimonialParte(
    tenantId: string,
    termo: string,
    comarca?: string
  ): Observable<FichaPatrimonialParte> {
    const q = new URLSearchParams({ termo });
    if (comarca) q.set('comarca', comarca);
    return this.http.get<FichaPatrimonialParte>(
      `${this.baseUrl}/workspaces/partes/ficha-patrimonial?${q.toString()}`,
      this.headersTenant(tenantId)
    );
  }

  sincronizarFeriadosOficiais(
    tenantId: string,
    ano = 2026,
    siglaTribunal = 'TJSP'
  ): Observable<{ sincronizados: number; suspensoesLocais: SuspensaoForenseLocal[] }> {
    return this.http.post<{ sincronizados: number; suspensoesLocais: SuspensaoForenseLocal[] }>(
      `${this.baseUrl}/workspaces/suspensoes/sincronizar-oficiais`,
      { ano, siglaTribunal },
      this.headersTenant(tenantId)
    );
  }

  exportarBackupWorkspace(tenantId: string): Observable<EstadoWorkspaceTenant> {
    return this.http.get<EstadoWorkspaceTenant>(
      `${this.baseUrl}/workspaces/backup`,
      this.headersTenant(tenantId)
    );
  }

  restaurarBackupWorkspace(
    tenantId: string,
    snapshot: EstadoWorkspaceTenant,
    mesclarComExistente = true
  ): Observable<EstadoWorkspaceTenant> {
    return this.http.post<EstadoWorkspaceTenant>(
      `${this.baseUrl}/workspaces/restore`,
      { snapshot, mesclarComExistente },
      this.headersTenant(tenantId)
    );
  }

  gerarMinutaProcessual(
    tenantId: string,
    payload: {
      teseId: string;
      numeroProcesso: string;
      classeProcessual?: string;
      assuntoPrincipal?: string;
      orgaoJulgador?: string;
      poloAtivo?: string;
      poloPassivo?: string;
      ultimoAndamento?: string;
      dataDisponibilizacaoIso?: string;
      diasPrazo?: number;
    }
  ): Observable<MinutaProcessualGerada> {
    return this.http.post<MinutaProcessualGerada>(
      `${this.baseUrl}/workspaces/teses/gerar-minuta`,
      payload,
      this.headersTenant(tenantId)
    );
  }

  calcularAtualizacaoFinanceiraProcesso(
    tenantId: string,
    numeroProcesso: string,
    valorOriginal: number,
    dataBaseIso?: string,
    dataFinalIso?: string
  ): Observable<ResultadoCalculoFinanceiroProcesso> {
    const q = new URLSearchParams({
      numeroProcesso,
      valorOriginal: String(valorOriginal || 157882.05)
    });
    if (dataBaseIso) q.set('dataBaseIso', dataBaseIso);
    if (dataFinalIso) q.set('dataFinalIso', dataFinalIso);
    return this.http.get<ResultadoCalculoFinanceiroProcesso>(
      `${this.baseUrl}/workspaces/processos/calculo-financeiro?${q.toString()}`,
      this.headersTenant(tenantId)
    );
  }
}
