using Markdig;
using Microsoft.AspNetCore.Components;

namespace Forge.Web.Services;

public class MarkdownService
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public MarkupString Render(string markdown)
    {
        return new MarkupString(Markdown.ToHtml(markdown ?? string.Empty, _pipeline));
    }
}
