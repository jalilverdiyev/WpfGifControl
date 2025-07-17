namespace WpfGifControl.Decoding;

public class GifHeader
{
	public long HeaderSize;
	internal int Iterations = -1;
	public GifRepeatBehavior? IterationCount;
	public GifRect Dimensions;
	public GifColor[]? GlobalColorTable;
}
