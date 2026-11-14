using Bunit;
using PosterPrintRequest.Web.Components.Pages;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class PosterRetentionDialogTests : BunitContext
{
    [Fact]
    public void Delete_shows_the_poster_and_waits_for_confirmation()
    {
        var deletes = 0;
        var cut = Render<PosterRetentionActions>(parameters => parameters
            .Add(component => component.PosterName, "Ada Lovelace - POSTER-2026-000810")
            .Add(component => component.CanDelete, true)
            .Add(component => component.ConfirmDelete, () => deletes++));

        cut.Find("[aria-label='Delete Ada Lovelace - POSTER-2026-000810']").Click();
        Assert.Equal(0, deletes);
        var dialog = cut.Find("[role='dialog']");
        Assert.Contains("Delete this poster?", dialog.TextContent, StringComparison.Ordinal);
        Assert.Contains("Ada Lovelace - POSTER-2026-000810", dialog.TextContent, StringComparison.Ordinal);

        cut.Find("button.epx-button:not(.epx-button-primary)").Click();
        Assert.Equal(0, deletes);
        Assert.Empty(cut.FindAll("[role='dialog']"));

        cut.Find("[aria-label='Delete Ada Lovelace - POSTER-2026-000810']").Click();
        cut.Find("button.epx-button-primary").Click();
        Assert.Equal(1, deletes);
        Assert.Empty(cut.FindAll("[role='dialog']"));
    }

    [Fact]
    public void Restore_is_offered_only_while_the_poster_is_retained()
    {
        var restores = 0;
        var retained = Render<PosterRetentionActions>(parameters => parameters
            .Add(component => component.PosterName, "Ada Lovelace - POSTER-2026-000811")
            .Add(component => component.CanRestore, true)
            .Add(component => component.Restore, () => restores++));

        Assert.Empty(retained.FindAll("[aria-label^='Delete ']"));
        retained.Find("[aria-label='Restore Ada Lovelace - POSTER-2026-000811']").Click();
        Assert.Equal(1, restores);

        var active = Render<PosterRetentionActions>(parameters => parameters
            .Add(component => component.PosterName, "Ada Lovelace - POSTER-2026-000812")
            .Add(component => component.CanDelete, true));
        Assert.Empty(active.FindAll("[aria-label^='Restore ']"));
    }
}
