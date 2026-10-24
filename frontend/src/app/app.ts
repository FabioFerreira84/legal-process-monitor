import { Component, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ProcessoService } from './services/processo.service';
import { Processo } from './models/processo.model';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly processoService = inject(ProcessoService);

  numeroProcessoInput = signal('2101760-84.2025.8.26.0000');
  carregando = signal(false);
  erro = signal<string | null>(null);
  processoAtual = signal<Processo | null>(null);
  processosRecentes = signal<Processo[]>([]);
  abaAtiva = signal<'decisoes' | 'timeline' | 'detalhes'>('decisoes');
  filtroTextoTimeline = signal('');
  copiado = signal(false);

  // Computa movimentações filtradas com busca textual
  movimentacoesFiltradas = computed(() => {
    const proc = this.processoAtual();
    if (!proc) return [];
    const filtro = this.filtroTextoTimeline().toLowerCase().trim();
    if (!filtro) return proc.movimentacoes;

    return proc.movimentacoes.filter(m =>
      m.titulo.toLowerCase().includes(filtro) ||
      m.descricao?.toLowerCase().includes(filtro)
    );
  });

  constructor() {
    this.carregarRecentes();
  }

  carregarRecentes() {
    this.processoService.listarRecentes(6).subscribe({
      next: (dados) => this.processosRecentes.set(dados),
      error: () => {}
    });
  }

  usarExemplo(exemplo: string) {
    this.numeroProcessoInput.set(exemplo);
    this.buscarProcesso();
  }

  formatarEntrada(evento: Event) {
    const input = evento.target as HTMLInputElement;
    let valor = input.value.replace(/\D/g, '');

    if (valor.length > 20) {
      valor = valor.substring(0, 20);
    }

    let formatado = valor;
    if (valor.length > 7) formatado = `${valor.substring(0, 7)}-${valor.substring(7)}`;
    if (valor.length > 9) formatado = `${formatado.substring(0, 10)}.${valor.substring(9)}`;
    if (valor.length > 13) formatado = `${formatado.substring(0, 15)}.${valor.substring(13)}`;
    if (valor.length > 14) formatado = `${formatado.substring(0, 17)}.${valor.substring(14)}`;
    if (valor.length > 16) formatado = `${formatado.substring(0, 20)}.${valor.substring(16)}`;

    this.numeroProcessoInput.set(formatado);
    input.value = formatado;
  }

  copiarNumero() {
    const proc = this.processoAtual();
    if (!proc) return;
    navigator.clipboard.writeText(proc.numeroCnjFormatado);
    this.copiado.set(true);
    setTimeout(() => this.copiado.set(false), 2000);
  }

  buscarProcesso(forcar = false) {
    const raw = this.numeroProcessoInput().trim();
    if (!raw) {
      this.erro.set('Por favor, digite o número do processo.');
      return;
    }

    this.carregando.set(true);
    this.erro.set(null);

    this.processoService.consultarProcesso(raw, forcar).subscribe({
      next: (processo) => {
        this.processoAtual.set(processo);
        this.carregando.set(false);
        this.abaAtiva.set(processo.documentosPublicos && processo.documentosPublicos.length > 0 ? 'decisoes' : 'timeline');
        this.carregarRecentes();
      },
      error: (err) => {
        this.carregando.set(false);
        if (err.error?.erro) {
          this.erro.set(err.error.erro);
        } else if (err.error?.mensagem) {
          this.erro.set(err.error.mensagem);
        } else {
          this.erro.set('Não foi possível se comunicar com o backend na nuvem. A instância pode estar aquecendo no Render, tente novamente em alguns instantes.');
        }
      }
    });
  }
}
