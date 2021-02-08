using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices
{
    public interface IModuleTypeService
    {
        bool TryGetType(string typeName, out IType type);
    }
}
