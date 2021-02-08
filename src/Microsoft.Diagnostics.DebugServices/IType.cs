using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices
{
    public interface IType
    {
        string Name { get; }
        string ModuleName { get; }
        List<IField> Fields { get; }

        bool TryGetField(string fieldName, out IField field);
    }
}
