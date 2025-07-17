namespace WpfGifControl.Exceptions;

[Serializable]
public sealed class InvalidGifStreamException(string message) : Exception(message);
