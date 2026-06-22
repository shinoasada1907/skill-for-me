// ============================================================================
// Smartis SAP B1 — AddOnVersionModel (canonical copy from VASManager)
// Bundled by skill: smartis-b1-license-scaffold
// Static identity (Name/Version/Partner) that every license query keys on.
// Populate it ONCE at startup from the add-on's .ard file (ExtName/ExtVersion/Partner)
// BEFORE any CheckLicense / license query runs — see references/integration-guide.md.
// ADAPT: namespace.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VASManager.Models
{
    class AddOnVersionModel
    {
        public static string Name { get; set; }
        public static string Version { get; set; }
        public static string Partner { get; set; }

    }
}
