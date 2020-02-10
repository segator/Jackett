using System;
using System.Collections.Generic;
using System.Text;

namespace Jackett.Common.Models.IndexerConfig.Bespoke
{
    class ConfigurationDataMuleAPI : ConfigurationData
    {
        public StringItem Password { get; private set; }
        public ConfigurationDataMuleAPI()
        {
            Password = new StringItem { Name = "Password", Value = "" };
        }
    }
}
