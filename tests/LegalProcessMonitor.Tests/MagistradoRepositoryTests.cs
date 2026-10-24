using System.Threading.Tasks;
using LegalProcessMonitor.Infrastructure.Repositories;
using Xunit;

namespace LegalProcessMonitor.Tests;

public class MagistradoRepositoryTests
{
    [Fact]
    public async Task ObterPorNome_DeveRetornarMagistradoQuandoExistir()
    {
        var repo = new InMemoryMagistradoRepository();

        var preuss = await repo.ObterPorNomeAsync("Pedro Paulo Maillet Preuss");
        Assert.NotNull(preuss);
        Assert.Equal("TJSP", preuss.Tribunal);
        Assert.Equal("24ª Câmara de Direito Privado", preuss.VaraOuCamara);
        Assert.NotEmpty(preuss.DicasPeticionamento);
        Assert.NotNull(preuss.BalcaoVirtualUrl);

        var alex = await repo.ObterPorNomeAsync("Alex Freitas Lima");
        Assert.NotNull(alex);
        Assert.Equal("1ª Vara Cível de Guarulhos", alex.VaraOuCamara);
    }

    [Fact]
    public async Task ObterPorVaraOuCamara_DeveLocalizarMagistradoCorreto()
    {
        var repo = new InMemoryMagistradoRepository();

        var mag = await repo.ObterPorVaraOuCamaraAsync("TJSP", "24ª Câmara");
        Assert.NotNull(mag);
        Assert.Contains("Preuss", mag.Nome);
    }
}
