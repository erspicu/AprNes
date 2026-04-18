using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace EnigmaBenchmarkAvalonia;

/// <summary>
/// Two-line monospace display with a dramatic reveal animation.
///
///   CIPHERTEXT (as intercepted)
///     SPLSRRYCCYOZYLTEGGMMEUQWPFRCGSFDHCBIZTE...   ← grey, constant
///   DECRYPTED
///     ANALLEBOOTE GRUPPE NORDWIND KURS NEUN...     ← green, fills left-to-right
///     "To all boats, Group Nordwind, course nine…" ← italic translation caption
///
/// The benchmark runs in milliseconds, but humans need about a second to
/// perceive a transformation. So after the GPU crack finishes we pace the
/// reveal at ~1.2 seconds so the eye catches it.
/// </summary>
public class CipherRevealPanel : Border
{
    readonly TextBlock _cipherText;
    readonly TextBlock _plainText;
    readonly TextBlock _translation;

    string _cipher = "";
    string _plain  = "";

    // Preview cap — 120 chars is enough to sell the reveal without the
    // panel eating the whole window.
    const int PreviewChars = 120;

    public CipherRevealPanel()
    {
        BorderBrush = Brush.Parse("#1F2430");
        BorderThickness = new Avalonia.Thickness(1);
        Background = Brush.Parse("#101418");
        CornerRadius = new Avalonia.CornerRadius(6);
        Padding = new Avalonia.Thickness(14, 10);

        var stack = new StackPanel { Spacing = 4 };

        stack.Children.Add(MakeLabel("CIPHERTEXT (as intercepted)"));
        _cipherText = MakeMono("#8A93A0");
        stack.Children.Add(_cipherText);

        stack.Children.Add(MakeLabel("DECRYPTED", topMargin: 8));
        _plainText = MakeMono("#80FF80");
        stack.Children.Add(_plainText);

        _translation = new TextBlock
        {
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            Foreground = Brush.Parse("#8A93A0"),
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        stack.Children.Add(_translation);

        Child = stack;
    }

    static TextBlock MakeLabel(string text, double topMargin = 0) => new()
    {
        Text = text,
        FontSize = 10.5,
        FontWeight = FontWeight.SemiBold,
        Foreground = Brush.Parse("#6A7280"),
        Margin = new Avalonia.Thickness(0, topMargin, 0, 0),
    };

    static TextBlock MakeMono(string colour) => new()
    {
        FontFamily = new FontFamily("Consolas,Cascadia Mono,monospace"),
        FontSize = 13.5,
        Foreground = Brush.Parse(colour),
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 20,
    };

    /// <summary>Called before benchmark: show cipher (grey), clear plaintext.</summary>
    public void SetCipher(byte[] ciphertext)
    {
        int n = Math.Min(ciphertext.Length, PreviewChars);
        char[] buf = new char[n];
        for (int i = 0; i < n; i++) buf[i] = (char)('A' + ciphertext[i]);
        _cipher = new string(buf);
        _cipherText.Text = _cipher + (ciphertext.Length > n ? "…" : "");
        _plainText.Text = "";
        _translation.Text = "";
    }

    /// <summary>Kick off the reveal animation. Completes when every character is shown.</summary>
    public async Task RevealAsync(byte[] plaintext, string translationCaption)
    {
        int n = Math.Min(plaintext.Length, PreviewChars);
        char[] buf = new char[n];
        for (int i = 0; i < n; i++) buf[i] = (char)('A' + plaintext[i]);
        _plain = new string(buf);

        // 1.2 s total, batched so we don't hammer the UI thread per-char.
        const double totalMs = 1200.0;
        int batchSize = Math.Max(1, n / 80);
        int batches = (n + batchSize - 1) / batchSize;
        int delayMs = Math.Max(8, (int)(totalMs / batches));

        var tcs = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        int shown = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
        timer.Tick += (s, _) =>
        {
            try
            {
                shown = Math.Min(n, shown + batchSize);
                _plainText.Text = _plain.Substring(0, shown);
                if (shown >= n)
                {
                    timer.Stop();
                    _translation.Text = translationCaption;
                    tcs.TrySetResult();
                }
            }
            catch (Exception ex)
            {
                // Log and unblock the await so we don't silently hang.
                Console.Error.WriteLine("[CipherRevealPanel tick] " + ex);
                timer.Stop();
                tcs.TrySetException(ex);
            }
        };
        timer.Start();
        await tcs.Task;
    }

    /// <summary>Reset to empty state.</summary>
    public void Reset()
    {
        _cipherText.Text = "";
        _plainText.Text = "";
        _translation.Text = "";
    }
}
