using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Warden.Core.Models
{
    /// <summary>
    /// A simple model used to filter processes out of a based on their name or path
    /// </summary>
    public class ProcessFilter
    {
        /// <summary>
        /// The name of the process with its extension 
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The path of the directory you wish to exclude from the tree, any process in this path will not be added.
        /// </summary>
        public string Path { get; set; }
    }
}
