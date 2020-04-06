namespace Warden.Core.Extensions
{
    /// <summary>
    /// 
    /// </summary>
    internal static class WmiExtensions
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
