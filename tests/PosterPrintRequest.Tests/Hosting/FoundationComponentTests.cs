using Bunit;
using PosterPrintRequest.Web.Components.Foundation;

namespace PosterPrintRequest.Tests.Hosting;

public sealed class FoundationComponentTests : TestContext
{
    [Fact]
    public void Text_field_exposes_label_help_and_error()
    {
        var cut = RenderComponent<TextField>(parameters => parameters
            .Add(field => field.FieldId, "requester-name")
            .Add(field => field.Label, "Name")
            .Add(field => field.Required, true)
            .Add(field => field.Help, "Student or staff name")
            .Add(field => field.Error, "Enter a name"));

        var input = cut.Find("input");
        Assert.Equal("requester-name", input.GetAttribute("id"));
        Assert.Equal("requester-name", cut.Find("label").GetAttribute("for"));
        Assert.Equal("true", input.GetAttribute("aria-required"));
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal("requester-name-help requester-name-error", input.GetAttribute("aria-describedby"));
        Assert.Contains("Student or staff name", cut.Find(".epx-helper-text").TextContent);
        Assert.Contains("Enter a name", cut.Find("[role='alert']").TextContent);
        Assert.Contains("required", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Select_field_keeps_a_blank_option_and_reports_an_error()
    {
        var cut = RenderComponent<SelectField>(parameters => parameters
            .Add(field => field.FieldId, "reason")
            .Add(field => field.Label, "Reason / Event")
            .Add(field => field.BlankLabel, "None")
            .Add(field => field.Value, "")
            .Add(field => field.Error, "Choose a reason")
            .Add(field => field.Options, new[]
            {
                new SelectOption("crd", "CRD")
            }));

        var options = cut.FindAll("option");
        Assert.Equal(2, options.Count);
        Assert.Equal("", options[0].GetAttribute("value"));
        Assert.Equal("None", options[0].TextContent);
        Assert.Equal("true", cut.Find("select").GetAttribute("aria-invalid"));
        Assert.Contains("epx-select-invalid", cut.Find("select").ClassList);
    }

    [Fact]
    public void Check_field_binds_its_label()
    {
        var cut = RenderComponent<CheckField>(parameters => parameters
            .Add(field => field.FieldId, "lamination")
            .Add(field => field.Label, "Lamination")
            .Add(field => field.Help, "Request lamination with the poster")
            .Add(field => field.Value, true));

        var input = cut.Find("input");
        Assert.Equal("checkbox", input.GetAttribute("type"));
        Assert.Equal("lamination", input.GetAttribute("id"));
        Assert.Equal("lamination", cut.Find("label").GetAttribute("for"));
        Assert.NotNull(input.GetAttribute("checked"));
        Assert.Equal("lamination-help", input.GetAttribute("aria-describedby"));
    }

    [Fact]
    public void Disabled_primary_button_cannot_be_used()
    {
        var cut = RenderComponent<ActionButton>(parameters => parameters
            .Add(button => button.Disabled, true)
            .Add(button => button.Variant, "primary")
            .AddChildContent("Continue"));

        var button = cut.Find("button");
        Assert.Contains("epx-button-primary", button.ClassList);
        Assert.NotNull(button.GetAttribute("disabled"));
        Assert.Equal("Continue", button.TextContent);
    }

    [Fact]
    public void Status_note_keeps_a_text_label_with_its_tone()
    {
        var cut = RenderComponent<StatusNote>(parameters => parameters
            .Add(note => note.Tone, "warning")
            .Add(note => note.Label, "Waiting")
            .AddChildContent("This poster is waiting to be printed."));

        Assert.Contains("epx-status-warning", cut.Markup);
        Assert.Contains("Waiting", cut.Find(".epx-semantic-indicator").TextContent);
        Assert.Contains("This poster is waiting to be printed.", cut.Markup);
    }

    [Fact]
    public void Current_home_link_exposes_the_page_state()
    {
        var cut = RenderComponent<ShellNavLink>(parameters => parameters
            .Add(link => link.Href, "/")
            .Add(link => link.MatchAll, true)
            .AddChildContent("Home"));

        var anchor = cut.Find("a");
        Assert.Equal("page", anchor.GetAttribute("aria-current"));
        Assert.Contains("active", anchor.ClassList);
        Assert.Equal("Home", anchor.TextContent);
    }
}
