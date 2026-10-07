using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PosterPrintRequest.Web.Components.Foundation;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class HelpComponentTests : TestContext
{
    [Fact]
    public void Good_and_rejected_examples_keep_a_text_outcome()
    {
        var good = RenderComponent<AcceptanceExample>(parameters => parameters
            .Add(example => example.Outcome, "good")
            .Add(example => example.Title, "One-page PDF")
            .AddChildContent("Exactly one page."));
        var rejected = RenderComponent<AcceptanceExample>(parameters => parameters
            .Add(example => example.Outcome, "rejected")
            .Add(example => example.Title, "Extra pages")
            .AddChildContent("More than one page."));

        Assert.Contains("Good", good.Find(".epx-semantic-indicator").TextContent);
        Assert.Contains("epx-acceptance-good", good.Markup);
        Assert.Contains("Not accepted", rejected.Find(".epx-semantic-indicator").TextContent);
        Assert.Contains("epx-status-danger", rejected.Markup);
    }

    [Fact]
    public void Contextual_help_links_to_its_topic()
    {
        var cut = RenderComponent<ContextualHelp>(parameters => parameters
            .Add(help => help.Title, "Width")
            .Add(help => help.TopicHref, "/help#poster-size")
            .Add(help => help.TopicLabel, "Review width")
            .AddChildContent("36 inches is the maximum width."));

        var link = cut.Find("a");
        Assert.Equal("/help#poster-size", link.GetAttribute("href"));
        Assert.Equal("Review width", link.TextContent);
        Assert.Contains("36 inches is the maximum width.", cut.Markup);
    }

    [Fact]
    public void Help_media_renders_only_supplied_content()
    {
        var empty = RenderComponent<HelpMedia>();
        Assert.DoesNotContain("<figure", empty.Markup, StringComparison.Ordinal);

        var imageWithoutAlt = RenderComponent<HelpMedia>(parameters => parameters
            .Add(media => media.ImageSrc, "/help/poster-preview.png"));
        Assert.DoesNotContain("<img", imageWithoutAlt.Markup, StringComparison.Ordinal);

        var image = RenderComponent<HelpMedia>(parameters => parameters
            .Add(media => media.ImageSrc, "/help/poster-preview.png")
            .Add(media => media.Alt, "Printer preview of a 36 by 72 inch poster")
            .Add(media => media.Caption, "914 × 1829 mm"));
        var img = image.Find("img");
        Assert.Equal("/help/poster-preview.png", img.GetAttribute("src"));
        Assert.Equal("Printer preview of a 36 by 72 inch poster", img.GetAttribute("alt"));
        Assert.Contains("914 × 1829 mm", image.Find("figcaption").TextContent);

        var video = RenderComponent<HelpMedia>(parameters => parameters
            .Add(media => media.VideoSrc, "/help/prepare-poster.mp4")
            .Add(media => media.CaptionSrc, "/help/prepare-poster.vtt")
            .Add(media => media.Transcript, "Set the poster to 36 inches wide and no more than 72 inches long."));
        Assert.Contains("/help/prepare-poster.vtt", video.Find("track").GetAttribute("src"));
        Assert.Contains("Set the poster to 36 inches wide", video.Markup);
    }

    [Fact]
    public void Help_link_is_current_on_the_help_route()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/help");

        var cut = RenderComponent<ShellNavLink>(parameters => parameters
            .Add(link => link.Href, "/help")
            .AddChildContent("Help"));

        Assert.Equal("page", cut.Find("a").GetAttribute("aria-current"));
    }
}
