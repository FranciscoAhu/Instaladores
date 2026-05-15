using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Instaladores
{
    public class ProgressToTextConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return "";

            if (values[0] is int progress && values[1] is bool success)
            {
                // ERROR → X
                if (!success && progress < 0)
                    return "X";

                // Normal → número
                return progress.ToString();
            }

            return "";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }


    public class ShowPercentageConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return Visibility.Visible;

            if (values[0] is int progress && values[1] is bool success)
            {
                // ERROR → ocultar %
                if (!success && progress < 0)
                    return Visibility.Collapsed;

                return Visibility.Visible;
            }

            return Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
