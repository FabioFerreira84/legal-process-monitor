import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Processo } from '../models/processo.model';

@Injectable({
  providedIn: 'root'
})
export class ProcessoService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = typeof window !== 'undefined' && window.location.hostname === 'localhost'
    ? 'http://localhost:5000/api/processos'
    : 'https://legal-process-monitor-api.onrender.com/api/processos';

  consultarProcesso(numeroProcesso: string, forcarAtualizacao = false): Observable<Processo> {
    const url = `${this.apiUrl}/${encodeURIComponent(numeroProcesso)}?forcarAtualizacao=${forcarAtualizacao}`;
    return this.http.get<Processo>(url);
  }

  listarRecentes(limite = 20): Observable<Processo[]> {
    return this.http.get<Processo[]>(`${this.apiUrl}?limite=${limite}`);
  }
}
