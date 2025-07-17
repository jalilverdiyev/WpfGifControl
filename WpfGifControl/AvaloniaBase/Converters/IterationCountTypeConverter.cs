using System.ComponentModel;
using System.Globalization;

namespace WpfGifControl.AvaloniaBase.Converters;

public class IterationCountTypeConverter : TypeConverter
{
	public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
		=> sourceType == typeof(string);

	public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
		=> IterationCount.Parse((string)value);
}
