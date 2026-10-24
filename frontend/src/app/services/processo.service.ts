import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Processo, AdvogadoCarteira, PerfilMagistrado } from '../models/processo.model';

@Injectable({
  providedIn: 'root'
})
export class ProcessoService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = typeof window !== 'undefined' && window.location.hostname === 'localhost'
    ? 'http://localhost:5000/api'
    : 'https://legal-process-monitor-api.onrender.com/api';

  consultarProcesso(numeroProcesso: string, forcarAtualizacao = false): Observable<Processo> {
    const url = `${this.baseUrl}/processos/${encodeURIComponent(numeroProcesso)}?forcarAtualizacao=${forcarAtualizacao}`;
    return this.http.get<Processo>(url);
  }

  consultarCarteiraOab(numeroOab: string): Observable<AdvogadoCarteira> {
    const url = `${this.baseUrl}/advogados/${encodeURIComponent(numeroOab)}`;
    return this.http.get<AdvogadoCarteira>(url);
  }

  obterMagistradoPorNome(nome: string): Observable<PerfilMagistrado> {
    const url = `${this.baseUrl}/magistrados/busca?nome=${encodeURIComponent(nome)}`;
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
}
