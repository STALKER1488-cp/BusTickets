using System.Globalization;
using System.Threading;
using System.Windows;

namespace BusTicketApp
{
    public partial class App : Application
    {
        public App()
        {
            // Принудительно устанавливаем российские стандарты форматирования (включая рубль)
            CultureInfo culture = new CultureInfo("ru-RU");
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            // Регистрируем культуру для WPF-элементов управления
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        }
    }
}
