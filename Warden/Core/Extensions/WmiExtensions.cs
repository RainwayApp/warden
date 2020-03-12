using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Warden.Core.Extensions
{
    /// <summary>
    /// 
    /// </summary>
    public static class WmiExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="wmiObj"></param>
        /// <param name="propertyName"></param>
        /// <returns></returns>
        public static T TryGetProperty<T>(this System.Management.ManagementBaseObject wmiObj, string propertyName)
        {
            try
            {
              return (T) wmiObj.GetPropertyValue(propertyName);
            }
            catch (System.Management.ManagementException)
            {
                //
            }
            return default;
        }
    }
}
