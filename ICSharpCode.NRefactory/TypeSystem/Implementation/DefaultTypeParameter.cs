﻿// Copyright (c) 2010 AlphaSierraPapa for the SharpDevelop Team (for details please see \doc\copyright.txt)
// This code is distributed under MIT X11 license (for details please see \doc\license.txt)

using System;
using System.Collections.Generic;
using System.Linq;

using ICSharpCode.NRefactory.Utils;

namespace ICSharpCode.NRefactory.TypeSystem.Implementation
{
	/// <summary>
	/// Default implementation of <see cref="ITypeParameter"/>.
	/// </summary>
	public sealed class DefaultTypeParameter : AbstractFreezable, ITypeParameter, ISupportsInterning
	{
		string name;
		int index;
		IList<ITypeReference> constraints;
		IList<IAttribute> attributes;
		
		DomRegion region;
		
		// Small fields: byte+byte+short
		VarianceModifier variance;
		EntityType ownerType;
		BitVector16 flags;
		
		const ushort FlagReferenceTypeConstraint      = 0x0001;
		const ushort FlagValueTypeConstraint          = 0x0002;
		const ushort FlagDefaultConstructorConstraint = 0x0004;
		
		protected override void FreezeInternal()
		{
			constraints = FreezeList(constraints);
			attributes = FreezeList(attributes);
			base.FreezeInternal();
		}
		
		public DefaultTypeParameter(EntityType ownerType, int index, string name)
		{
			if (!(ownerType == EntityType.TypeDefinition || ownerType == EntityType.Method))
				throw new ArgumentException("owner must be a type or a method", "ownerType");
			if (index < 0)
				throw new ArgumentOutOfRangeException("index", index, "Value must not be negative");
			if (name == null)
				throw new ArgumentNullException("name");
			this.ownerType = ownerType;
			this.index = index;
			this.name = name;
		}
		
		public TypeKind Kind {
			get { return TypeKind.TypeParameter; }
		}
		
		public string Name {
			get { return name; }
		}
		
		string INamedElement.FullName {
			get { return name; }
		}
		
		string INamedElement.Namespace {
			get { return string.Empty; }
		}
		
		public string ReflectionName {
			get {
				if (ownerType == EntityType.Method)
					return "``" + index.ToString();
				else
					return "`" + index.ToString();
			}
		}
		
		public bool? IsReferenceType(ITypeResolveContext context)
		{
			switch (flags.Data & (FlagReferenceTypeConstraint | FlagValueTypeConstraint)) {
				case FlagReferenceTypeConstraint:
					return true;
				case FlagValueTypeConstraint:
					return false;
			}
			// protect against cyclic dependencies between type parameters
			using (var busyLock = BusyManager.Enter(this)) {
				if (busyLock.Success) {
					foreach (ITypeReference constraintRef in this.Constraints) {
						IType constraint = constraintRef.Resolve(context);
						ITypeDefinition constraintDef = constraint.GetDefinition();
						// While interfaces are reference types, an interface constraint does not
						// force the type parameter to be a reference type; so we need to explicitly look for classes here.
						if (constraintDef != null && constraintDef.Kind == TypeKind.Class)
							return true;
						if (constraint is ITypeParameter) {
							bool? isReferenceType = constraint.IsReferenceType(context);
							if (isReferenceType.HasValue)
								return isReferenceType.Value;
						}
					}
				}
			}
			return null;
		}
		
		int IType.TypeParameterCount {
			get { return 0; }
		}
		
		IType IType.DeclaringType {
			get { return null; }
		}
		
		ITypeDefinition IType.GetDefinition()
		{
			return null;
		}
		
		IType ITypeReference.Resolve(ITypeResolveContext context)
		{
			return this;
		}
		
		public override int GetHashCode()
		{
			unchecked {
				return (int)ownerType * 178256151 + index;
			}
		}
		
		public override bool Equals(object obj)
		{
			return Equals(obj as IType);
		}
		
		public bool Equals(IType other)
		{
			DefaultTypeParameter p = other as DefaultTypeParameter;
			if (p == null)
				return false;
			return ownerType == p.ownerType && index == p.index;
		}
		
		public EntityType OwnerType {
			get {
				return ownerType;
			}
		}
		
		public int Index {
			get { return index; }
		}
		
		public IList<IAttribute> Attributes {
			get {
				if (attributes == null)
					attributes = new List<IAttribute>();
				return attributes;
			}
		}
		
		public IList<ITypeReference> Constraints {
			get {
				if (constraints == null)
					constraints = new List<ITypeReference>();
				return constraints;
			}
		}
		
		public bool HasDefaultConstructorConstraint {
			get { return flags[FlagDefaultConstructorConstraint]; }
			set {
				CheckBeforeMutation();
				flags[FlagDefaultConstructorConstraint] = value;
			}
		}
		
		public bool HasReferenceTypeConstraint {
			get { return flags[FlagReferenceTypeConstraint]; }
			set {
				CheckBeforeMutation();
				flags[FlagReferenceTypeConstraint] = value;
			}
		}
		
		public bool HasValueTypeConstraint {
			get { return flags[FlagValueTypeConstraint]; }
			set {
				CheckBeforeMutation();
				flags[FlagValueTypeConstraint] = value;
			}
		}
		
		public VarianceModifier Variance {
			get { return variance; }
			set {
				CheckBeforeMutation();
				variance = value;
			}
		}
		
		public DomRegion Region {
			get { return region; }
			set {
				CheckBeforeMutation();
				region = value;
			}
		}
		
		public IType AcceptVisitor(TypeVisitor visitor)
		{
			return visitor.VisitTypeParameter(this);
		}
		
		public IType VisitChildren(TypeVisitor visitor)
		{
			return this;
		}
		
		static readonly SimpleProjectContent dummyProjectContent = new SimpleProjectContent();
		
		DefaultTypeDefinition GetDummyClassForTypeParameter()
		{
			DefaultTypeDefinition c = new DefaultTypeDefinition(dummyProjectContent, string.Empty, this.Name);
			c.Region = this.Region;
			if (HasValueTypeConstraint) {
				c.Kind = TypeKind.Struct;
			} else if (HasDefaultConstructorConstraint) {
				c.Kind = TypeKind.Class;
			} else {
				c.Kind = TypeKind.Interface;
			}
			return c;
		}
		
		public IEnumerable<IMethod> GetConstructors(ITypeResolveContext context, Predicate<IMethod> filter = null)
		{
			if (HasDefaultConstructorConstraint || HasValueTypeConstraint) {
				DefaultMethod m = DefaultMethod.CreateDefaultConstructor(GetDummyClassForTypeParameter());
				if (filter(m))
					return new [] { m };
			}
			return EmptyList<IMethod>.Instance;
		}
		
		public IEnumerable<IMethod> GetMethods(ITypeResolveContext context, Predicate<IMethod> filter = null)
		{
			foreach (var baseType in GetNonCircularBaseTypes(context)) {
				foreach (var m in baseType.GetMethods(context, filter)) {
					if (!m.IsStatic)
						yield return m;
				}
			}
		}
		
		public IEnumerable<IProperty> GetProperties(ITypeResolveContext context, Predicate<IProperty> filter = null)
		{
			foreach (var baseType in GetNonCircularBaseTypes(context)) {
				foreach (var m in baseType.GetProperties(context, filter)) {
					if (!m.IsStatic)
						yield return m;
				}
			}
		}
		
		public IEnumerable<IField> GetFields(ITypeResolveContext context, Predicate<IField> filter = null)
		{
			foreach (var baseType in GetNonCircularBaseTypes(context)) {
				foreach (var m in baseType.GetFields(context, filter)) {
					if (!m.IsStatic)
						yield return m;
				}
			}
		}
		
		public IEnumerable<IEvent> GetEvents(ITypeResolveContext context, Predicate<IEvent> filter = null)
		{
			foreach (var baseType in GetNonCircularBaseTypes(context)) {
				foreach (var m in baseType.GetEvents(context, filter)) {
					if (!m.IsStatic)
						yield return m;
				}
			}
		}
		
		public IEnumerable<IMember> GetMembers(ITypeResolveContext context, Predicate<IMember> filter = null)
		{
			foreach (var baseType in GetNonCircularBaseTypes(context)) {
				foreach (var m in baseType.GetMembers(context, filter)) {
					if (!m.IsStatic)
						yield return m;
				}
			}
		}
		
		// Problem with type parameter resolving - circular declarations
		//   void Example<S, T> (S s, T t) where S : T where T : S
		IEnumerable<IType> GetNonCircularBaseTypes(ITypeResolveContext context)
		{
			var result = this.GetBaseTypes(context).Where(bt => !IsCircular (context, bt));
			if (result.Any ())
				return result;
			
			// result may be empty, GetBaseTypes doesn't return object/struct when there are only constraints (even circular) as base types are available,
			// but even when there are only circular references the default base type should be included.
			IType defaultBaseType = context.GetTypeDefinition("System", HasValueTypeConstraint ? "ValueType" : "Object", 0, StringComparer.Ordinal);
			if (defaultBaseType != null)
				return new [] { defaultBaseType };
			return Enumerable.Empty<IType> ();
		}
		
		bool IsCircular(ITypeResolveContext context, IType baseType)
		{
			var parameter = baseType as DefaultTypeParameter;
			if (parameter == null)
				return false;
			var stack = new Stack<DefaultTypeParameter>();
			while (true) {
				if (parameter == this)
					return true;
				foreach (DefaultTypeParameter parameterBaseType in parameter.GetNonCircularBaseTypes(context).Where(t => t is DefaultTypeParameter)) {
					stack.Push(parameterBaseType);
				}
				if (stack.Count == 0)
					return false;
				parameter = stack.Pop();
			}
		}
		
		IEnumerable<IType> IType.GetNestedTypes(ITypeResolveContext context, Predicate<ITypeDefinition> filter)
		{
			return EmptyList<IType>.Instance;
		}
		
		public IEnumerable<IType> GetBaseTypes(ITypeResolveContext context)
		{
			bool hasNonInterfaceConstraint = false;
			foreach (ITypeReference constraint in this.Constraints) {
				IType c = constraint.Resolve(context);
				yield return c;
				if (c.Kind != TypeKind.Interface)
					hasNonInterfaceConstraint = true;
			}
			// Do not add the 'System.Object' constraint if there is another constraint with a base class.
			if (HasValueTypeConstraint || !hasNonInterfaceConstraint) {
				IType defaultBaseType = context.GetTypeDefinition("System", HasValueTypeConstraint ? "ValueType" : "Object", 0, StringComparer.Ordinal);
				if (defaultBaseType != null)
					yield return defaultBaseType;
			}
		}
		
		void ISupportsInterning.PrepareForInterning(IInterningProvider provider)
		{
			constraints = provider.InternList(constraints);
			attributes = provider.InternList(attributes);
		}
		
		int ISupportsInterning.GetHashCodeForInterning()
		{
			unchecked {
				int hashCode = GetHashCode();
				if (name != null)
					hashCode += name.GetHashCode();
				if (attributes != null)
					hashCode += attributes.GetHashCode();
				if (constraints != null)
					hashCode += constraints.GetHashCode();
				hashCode += 771 * flags.Data + 900103 * (int)variance;
				return hashCode;
			}
		}
		
		bool ISupportsInterning.EqualsForInterning(ISupportsInterning other)
		{
			DefaultTypeParameter o = other as DefaultTypeParameter;
			return o != null
				&& this.attributes == o.attributes
				&& this.constraints == o.constraints
				&& this.flags == o.flags
				&& this.ownerType == o.ownerType
				&& this.index == o.index
				&& this.variance == o.variance;
		}
		
		public override string ToString()
		{
			return this.ReflectionName;
		}
	}
}
