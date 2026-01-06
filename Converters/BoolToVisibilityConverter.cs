using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SalsaNOWGames.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter != null && parameter.ToString().ToLower() == "invert";
            
            // Handle boolean
            if (value is bool boolValue)
            {
                if (invert) boolValue = !boolValue;
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // Handle int (for Count properties)
            if (value is int intValue)
            {
                bool hasItems = intValue > 0;
                if (invert) hasItems = !hasItems;
                return hasItems ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // Handle string comparison (for CurrentView)
            if (value is string strValue && parameter != null)
            {
                string paramStr = parameter.ToString();
                if (paramStr.StartsWith("invert:"))
                {
                    string compareValue = paramStr.Substring(7);
                    return strValue != compareValue ? Visibility.Visible : Visibility.Collapsed;
                }
                return strValue == paramStr ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // Handle null/empty strings
            if (value == null || (value is string s && string.IsNullOrEmpty(s)))
            {
                return invert ? Visibility.Visible : Visibility.Collapsed;
            }
            
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                bool invert = parameter != null && parameter.ToString().ToLower() == "invert";
                return invert ? !result : result;
            }
            return false;
        }
    }

    public class BoolOrToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null) return Visibility.Collapsed;
            
            foreach (var value in values)
            {
                if (value is bool boolValue && boolValue)
                    return Visibility.Visible;
            }
            
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
