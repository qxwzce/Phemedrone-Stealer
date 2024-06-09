using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Phemedrone.Protections
{
    public class CISCheck
    {
        public static bool IsCIS()
        {
            var languages = InputLanguage.InstalledInputLanguages;
            var desiredCultures = new CultureInfo[]
            {
                // Ukraine no more CIS country but if u need u can add ukraine in block list just remove <//>
                //new("uk-UA"),
                
                new("ru-RU"),
                new("kk-KZ"),
                new("ro-MD"),
                new("uz-UZ"),
                new("be-BY"),
                new("az-Latn-AZ"),
                new("hy-AM"),
                new("ky-KG"),
                new("tg-Cyrl-TJ")
            };

            return languages.Cast<InputLanguage>().Any(language => Array.Exists(desiredCultures, culture => culture.Equals(language.Culture)));
        }
    }
}