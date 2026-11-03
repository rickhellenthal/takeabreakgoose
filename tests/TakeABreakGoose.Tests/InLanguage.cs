using System.Resources;
using Xunit;

// The app's language is one setting for everything, so tests that switch it can't run alongside others.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace TakeABreakGoose.Tests;

/// <summary>Speaks a language until disposed, then English again: <c>using var _ = InLanguage.Dutch();</c></summary>
internal sealed class InLanguage : IDisposable
{
    private InLanguage(string code) => Languages.Use(code);

    public static InLanguage Dutch() => new(Languages.Dutch);

    public void Dispose() => Languages.Use(Languages.English);
}
