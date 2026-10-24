import { Component, signal, inject } from '@angular/core';
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

  numeroProcessoInput = signal('');
  carregando = signal(false);
  erro = signal<string | null>(null);
  processoAtual = signal<Processo | null>(null);
  processosRecentes = signal<Processo[]>([]);

  constructor() {
    this.carregarRecentes();
  }

  carregarRecentes() {
    this.processoService.listarRecentes(10).subscribe({
      next: (dados) => this.processosRecentes.set(dados),
      error: () => {
        // Silencioso na inicialização caso a API ainda não esteja de pé
      }
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

    // Aplica máscara NNNNNNN-DD.AAAA.J.TR.OOOO
    let formatado = valor;
    if (valor.length > 7) {
      formatado = `${valor.substring(0, 7)}-${valor.substring(7)}`;
    }
    if (valor.length > 9) {
      formatado = `${formatado.substring(0, 10)}.${valor.substring(9)}`;
    }
    if (valor.length > 13) {
      formatado = `${formatado.substring(0, 15)}.${valor.substring(13)}`;
    }
    if (valor.length > 14) {
      formatado = `${formatado.substring(0, 17)}.${valor.substring(14)}`;
    }
    if (valor.length > 16) {
      formatado = `${formatado.substring(0, 20)}.${valor.substring(16)}`;
    }

    this.numeroProcessoInput.set(formatado);
    input.value = formatado;
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
        this.carregarRecentes();
      },
      error: (err) => {
        this.carregando.set(false);
        if (err.error?.erro) {
          this.erro.set(err.error.erro);
        } else if (err.error?.mensagem) {
          this.erro.set(err.error.mensagem);
        } else {
          this.erro.set('Não foi possível se comunicar com o servidor da API. Verifique se o backend está em execução.');
        }
      }
    });
  }
}
