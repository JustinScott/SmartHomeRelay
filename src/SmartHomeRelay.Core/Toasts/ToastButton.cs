namespace SmartHomeRelay.Core.Toasts;

/// <summary>A toast button (spec 5.6).</summary>
/// <param name="Label">The text on the button.</param>
/// <param name="Arguments">An http(s) URL Windows opens itself, or the arguments the app receives when <paramref name="OpensUrl"/> is false.</param>
/// <param name="OpensUrl">True: Windows opens <paramref name="Arguments"/> as a URL. False: the click is sent to the running app.</param>
public sealed record ToastButton(string Label, string Arguments, bool OpensUrl);
