// Marks a serialized reference that is allowed to stay empty.
//
// SceneWiringTests asserts that every serialized reference in the generated scene is filled,
// which is what catches a builder that forgot a field. A few references are genuinely
// optional - a text-only button has no icon - and this attribute says so explicitly, so the
// guard keeps its teeth everywhere else rather than being loosened by a name blacklist.
using System;
using UnityEngine;

namespace DustZero.Game
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class OptionalReferenceAttribute : PropertyAttribute
    {
        /// <summary>Why it may be empty. Shown in the wiring test's failure output.</summary>
        public string Reason { get; }

        public OptionalReferenceAttribute(string reason) => Reason = reason;
    }
}
