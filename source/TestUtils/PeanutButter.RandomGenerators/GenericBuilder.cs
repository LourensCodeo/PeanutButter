﻿/* Generic Builder base class
 * author: Davyd McColl (davydm@gmail.com)
 * license: BSD
 * */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using PeanutButter.TestUtils.Generic;
using PeanutButter.Utils;


namespace PeanutButter.RandomGenerators
{
    /// <summary>
    /// Base class for builders to produce instance of objects with a fluent
    /// builder-like syntax. Also includes utilities like randomizing property
    /// values.
    /// </summary>
    /// <typeparam name="TBuilder">Concrete type of the current builder, required to be able to return the builder from all With* methods</typeparam>
    /// <typeparam name="TEntity">Type of entity this builder builds</typeparam>
    public class GenericBuilder<TBuilder, TEntity> : GenericBuilderBase, IGenericBuilder, IBuilder<TEntity>
        where TBuilder : GenericBuilder<TBuilder, TEntity>
    {
        private static readonly List<Action<TEntity>> _defaultPropMods = new List<Action<TEntity>>();
        private readonly List<Action<TEntity>> _propMods = new List<Action<TEntity>>();
        private static Type _constructingTypeBackingField = typeof(TEntity);

        private static Type ConstructingType
        {
            get { return _constructingTypeBackingField; }
            set
            {
                lock (_lockObject)
                {
                    _constructingTypeBackingField = value;
                    _randomPropSettersField = null;
                    _entityPropInfoField = null;
                }
            }
        }


        /// <summary>
        /// Creates a new instance of the builder; used to provide a fluent syntax
        /// </summary>
        /// <returns>New instance of the builder</returns>
        public static TBuilder Create()
        {
            return Activator.CreateInstance<TBuilder>();
        }

        /// <inheritdoc />
        public IGenericBuilder GenericWithRandomProps()
        {
            return _buildLevel > MaxRandomPropsLevel
                ? this
                : WithRandomProps();
        }

        /// <inheritdoc />
        public IGenericBuilder WithBuildLevel(int level)
        {
            _buildLevel = level;
            return this;
        }

        /// <inheritdoc />
        public object GenericBuild()
        {
            return Build();
        }

        /// <summary>
        /// Builds a default instance of the entity
        /// </summary>
        /// <returns>New instance of the builder entity</returns>
        public static TEntity BuildDefault()
        {
            return Create().Build();
        }

        /// <summary>
        /// Convenience method: Creates a builder, sets random properties, returns a new instance of the entity
        /// </summary>
        /// <returns>New instance of TEntity with all randomizable properties randomized</returns>
        public static TEntity BuildRandom()
        {
            return Create().WithRandomProps().Build();
        }

        // ReSharper disable once UnusedMember.Global
        /// <summary>
        /// Adds a default property setter, shared amongst all instances of this 
        /// particular builder type
        /// </summary>
        /// <param name="action">
        /// Action to perform on the entity being built, will run before any
        /// actions specified on the instance
        /// </param>
        public static void WithDefaultProp(Action<TEntity> action)
        {
            _defaultPropMods.Add(action);
        }

        /// <summary>
        /// Generric method to set a property on the entity.
        /// </summary>
        /// <param name="action">Action to run on the entity at build time</param>
        /// <returns>The current instance of the builder</returns>
        public TBuilder WithProp(Action<TEntity> action)
        {
            _propMods.Add(action);
            return this as TBuilder;
        }

        // ReSharper disable once MemberCanBeProtected.Global
        // ReSharper disable once VirtualMemberNeverOverridden.Global
        /// <summary>
        /// Constructs a new instance of the entity. Mostly, an inheriter won't have to
        /// care, but if your entity has no parameterless constructor, you'll want to override
        /// this in your derived builder.
        /// </summary>
        /// <returns>New instance of TEntity, constructed from the parameterless constructor, when possible</returns>
        /// <exception cref="GenericBuilderInstanceCreationException"></exception>
        public virtual TEntity ConstructEntity()
        {
            try
            {
                return AttemptToConstructEntity();
            }
            catch (Exception)
            {
                throw new GenericBuilderInstanceCreationException(GetType(), typeof(TEntity));
            }
        }

        private static TEntity AttemptToConstructEntity()
        {
            try
            {
                return ConstructInCurrentDomain<TEntity>(ConstructingType);
            }
            catch (MissingMethodException ex)
            {
                try
                {
                    var constructed = AttemptToConstructWithImplementingType<TEntity>();
                    ConstructingType = constructed.GetType();
                    return constructed;
                }
                catch (Exception)
                {
                    try
                    {
                        return AttemptToCreateSubstituteFor<TEntity>();
                    }
                    catch (Exception)
                    {
                        throw ex;
                    }
                }
            }
        }

        private static T AttemptToCreateSubstituteFor<T>()
        {
            var loadedNsubstitute = FindOrLoadNSubstitute<T>();
            if (loadedNsubstitute == null)
                throw new Exception("Can't find (or load) NSubstitute )':");
            var subType = loadedNsubstitute.GetTypes().FirstOrDefault(t => t.Name == "Substitute");
            if (subType == null)
                throw new Exception("NSubstitute assembly loaded -- but no Substitute class? )':");
            var genericMethod = subType.GetMethods().FirstOrDefault(m => m.Name == "For" && IsObjectParams(m.GetParameters()));
            if (genericMethod == null)
                throw new Exception("Can't find NSubstitute.Substitute.For method )':");
            var specificMethod = genericMethod.MakeGenericMethod(typeof(T));
            return (T) specificMethod.Invoke(null, new object[] {new object[] {}});
        }

        private static Assembly FindOrLoadNSubstitute<T>(bool retrying = false)
        {
            var loadedNsubstitute = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "NSubstitute");
            if (loadedNsubstitute == null && !retrying)
            {
                AttemptToLoadNSubstitute<T>();
                return FindOrLoadNSubstitute<T>(true);
            }
            return loadedNsubstitute;
        }

        private static void AttemptToLoadNSubstitute<T>()
        {
            var codeBase = new Uri(typeof(T).Assembly.CodeBase).LocalPath;
            if (!File.Exists(codeBase))
                return;
            var folder = Path.GetDirectoryName(codeBase);
            var search = Path.Combine(folder ?? "", "NSubstitute.dll");
            if (!File.Exists(search))
                return;
            try
            {
                Assembly.Load(File.ReadAllBytes(search));
            }
            catch { /* Nothing much to be done here anyway */ }
        }

        private static bool IsObjectParams(ParameterInfo[] parameterInfos)
        {
            return parameterInfos.Length == 1 && parameterInfos[0].ParameterType == typeof(object[]);
        }

        private static T AttemptToConstructWithImplementingType<T>()
        {
            try
            {
                return TryCreateConcreteInstanceFromSameAssemblyAs<T>();
            }
            catch
            {
                return TryCreateConcreteInstanceFromAnyAssembly<T>();
            }
        }

        private static TInterface TryCreateConcreteInstanceFromSameAssemblyAs<TInterface>()
        {
            var assembly = typeof(TInterface).Assembly;
            var type = FindImplementingTypeFor<TInterface>(new[] { assembly });
            if (type == null)
                throw new TypeLoadException();
            return ConstructInCurrentDomain<TInterface>(type);
        }

        private static TInterface TryCreateConcreteInstanceFromAnyAssembly<TInterface>()
        {
            var type = FindImplementingTypeFor<TInterface>(AppDomain.CurrentDomain.GetAssemblies());
            if (type == null)
                throw new TypeLoadException();
            return ConstructInCurrentDomain<TInterface>(type);
        }

        private static TInterface ConstructInCurrentDomain<TInterface>(Type type)
        {
            var handle = Activator.CreateInstance(
                AppDomain.CurrentDomain,
                type.Assembly.FullName,
                type.FullName);
            return (TInterface)handle.Unwrap();
        }

        private static Type FindImplementingTypeFor<TInterface>(IEnumerable<Assembly> assemblies)
        {
            var interfaceType = typeof(TInterface);
            return assemblies.SelectMany(a =>
                {
                    try
                    {
                        return a.GetExportedTypes();
                    }
                    catch
                    {
                        return new Type[] { };
                    }
                })
                .FirstOrDefault(t => interfaceType.IsAssignableFrom(t) &&
                                     t.IsClass &&
                                     !t.IsAbstract &&
                                     t.HasDefaultConstructor());
        }

        /// <summary>
        /// Builds the instance of TEntity, applying all builder actions in
        /// order to provide the required entity
        /// </summary>
        /// <returns>An instance of TEntity with all builder actions run on it</returns>
        public virtual TEntity Build()
        {
            var entity = ConstructEntity();
            foreach (var action in _defaultPropMods.Union(_propMods))
            {
                action(entity);
            }
            return entity;
        }

        /// <summary>
        /// Randomizes all properties on the instance of TEntity being built.
        /// This method will use methods from RandomValueGen and may generate
        /// new GenericBuilder types for generating more complex properties
        /// </summary>
        /// <returns>The current builder instance</returns>
        public virtual TBuilder WithRandomProps()
        {
            WithProp(SetRandomProps);
            return this as TBuilder;
        }

        // ReSharper disable once StaticMemberInGenericType
        private static readonly object _lockObject = new object();

#pragma warning disable S2743 // Static fields should not be used in generic types
        // ReSharper disable once StaticMemberInGenericType
        private static PropertyInfo[] _entityPropInfoField;
        private static PropertyInfo[] EntityPropInfo
        {
            get
            {
                lock (_lockObject)
                {
                    return _entityPropInfoField ?? (_entityPropInfoField = ConstructingType.GetProperties());
                }
            }
        }
#pragma warning restore S2743 // Static fields should not be used in generic types

        private static Dictionary<string, Action<TEntity, int>> _randomPropSettersField;
        private static Dictionary<string, Action<TEntity, int>> RandomPropSetters
        {
            get
            {
                var entityProps = EntityPropInfo;
                lock (_lockObject)
                {
                    if (_randomPropSettersField != null) return _randomPropSettersField;
                    _randomPropSettersField = new Dictionary<string, Action<TEntity, int>>();
                    foreach (var prop in entityProps)
                    {
                        SetSetterForType(prop);
                    }
                    return _randomPropSettersField;
                }
            }
        }

        private static readonly Dictionary<Type, Func<PropertyInfo, Action<TEntity, int>>> _simpleTypeSetters =
            new Dictionary<Type, Func<PropertyInfo, Action<TEntity, int>>>()
            {
                { typeof (int), pi => ((e, i) => pi.SetValue(e, RandomValueGen.GetRandomInt(), null))},
                { typeof (long), pi => ((e, i) => pi.SetValue(e, RandomValueGen.GetRandomInt(), null))},
                { typeof (float), pi => ((e, i) => pi.SetValue(e, Convert.ToSingle(RandomValueGen.GetRandomDouble(float.MinValue, float.MaxValue), null))) },
                { typeof (double), pi => ((e, i) => pi.SetValue(e, RandomValueGen.GetRandomDouble(), null))},
                { typeof (decimal), pi => ((e, i) => pi.SetValue(e, RandomValueGen.GetRandomDecimal(), null))},
                { typeof(DateTime), pi => ((e, i) => pi.SetValue(e, RandomValueGen.GetRandomDate(), null))},
                { typeof(Guid), pi => ((e, i) => pi.SetValue(e, Guid.NewGuid(), null)) },
                { typeof(string), CreateStringPropertyRandomSetterFor },
                { typeof(bool), CreateBooleanPropertyRandomSetterFor },
                { typeof(byte[]), pi => ((e, i) => pi.SetValue(e, RandomValueGen.GetRandomBytes(), null)) }
            };

        private static Action<TEntity, int> CreateStringPropertyRandomSetterFor(PropertyInfo pi)
        {
            if (MayBeEmail(pi))
                return (e, i) => pi.SetValue(e, RandomValueGen.GetRandomEmail(), null);
            if (MayBeUrl(pi))
                return (e, i) => pi.SetValue(e, RandomValueGen.GetRandomHttpUrl(), null);
            if (MayBePhone(pi))
                return (e, i) => pi.SetValue(e, RandomValueGen.GetRandomNumericString(), null);
            return (e, i) => pi.SetValue(e, RandomValueGen.GetRandomString(), null);
        }

        private static bool MayBePhone(PropertyInfo pi)
        {
            return pi != null &&
                   (pi.Name.ContainsOneOf("phone", "mobile", "fax") ||
                    pi.Name.StartsWithOneOf("tel"));
        }

        private static bool MayBeUrl(PropertyInfo pi)
        {
            return pi != null &&
                   pi.Name.ContainsOneOf("url", "website");
        }

        private static bool MayBeEmail(PropertyInfo pi)
        {
            return pi != null && pi.Name.ToLower().Contains("email");
        }

        private static Action<TEntity, int> CreateBooleanPropertyRandomSetterFor(PropertyInfo pi)
        {
            if (pi.Name == "Enabled")
                return (e, i) => pi.SetValue(e, true, null);
            return (e, i) => pi.SetValue(e, RandomValueGen.GetRandomBoolean(), null);
        }

        private static bool IsCollectionType(PropertyInfo propertyInfo, Type type)
        {
            // TODO: figure out the stack overflow introduced by cyclic references when enabling this
            //  code below
            if (type.IsNotCollection())
                return false;
            SetCollectionSetterFor(propertyInfo);
            return true;
        }

        private static void SetCollectionSetterFor(PropertyInfo propertyInfo)
        {
            RandomPropSetters[propertyInfo.Name] = (e, i) =>
            {
                try
                {
                    var instance = CreateListContainerFor(propertyInfo);
                    if (propertyInfo.PropertyType.IsArray)
                    {
                        instance = ConvertCollectionToArray(instance);
                    }
                    e.SetPropertyValue(propertyInfo.Name, instance);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Unable to set Collection Setter for {propertyInfo.Name}: {ex.GetType().Name} : {ex.Message}");
                }
            };
        }

        private static object ConvertCollectionToArray(object instance)
        {
            var methodInfo = instance.GetType().GetMethod("ToArray");
            instance = methodInfo.Invoke(instance, new object[] { });
            return instance;
        }

        // ReSharper disable once VirtualMemberNeverOverridden.Global
        /// <summary>
        /// Attempts to fill collections with random data. May fail with stack-overflows
        /// on complex, cyclic-referencing objects. Use with caution.
        /// </summary>
        /// <returns>The current instance of the builder</returns>
        public virtual TBuilder WithFilledCollections()
        {
            return WithProp(o =>
            {
                // TODO: figure out why filling collections can cause an SO with cyclic classes
                var collectionProperties = typeof(TEntity).GetProperties()
                                                .Where(pi => IsCollectionType(pi, pi.PropertyType));
                collectionProperties.ForEach(prop => FillCollection(o, prop));
            });
        }

        private void FillCollection(object entity, PropertyInfo pi)
        {
            var container = CreateListContainerFor(pi);
            FillContainer(container);
            if (pi.PropertyType.IsArray)
                container = ConvertCollectionToArray(container);
            pi.SetValue(entity, container);
        }

        private static void FillContainer(object collectionInstance)
        {
            var innerType = collectionInstance.GetType().GetGenericArguments()[0];
            var method = collectionInstance.GetType().GetMethod("Add");
            var data = RandomValueGen.GetRandomCollection(() => RandomValueGen.GetRandomValue(innerType), 1);
            data.ForEach(item => method.Invoke(collectionInstance, new[] { item }));
        }

        private static object CreateListContainerFor(PropertyInfo propertyInfo)
        {
            var innerType = GetCollectionInnerTypeFor(propertyInfo);
            var listType = typeof(List<>);
            var specificType = listType.MakeGenericType(innerType);
            var instance = Activator.CreateInstance(specificType);
            return instance;
        }

        private static Type GetCollectionInnerTypeFor(PropertyInfo propertyInfo)
        {
            return propertyInfo.PropertyType.IsGenericType
                    ? propertyInfo.PropertyType.GetGenericArguments()[0]
                    : propertyInfo.PropertyType.GetElementType();
        }

        private static void SetSetterForType(PropertyInfo prop, Type propertyType = null)
        {
            foreach (var setter in _propertySetterStrategies)
            {
                if (setter(prop, propertyType ?? prop.PropertyType))
                    return;
            }

        }

        // whilst the collection itself does not reference a type parameter,
        //  HaveSetSimpleSetterFor does, so this collection must be per-generic-definition
        // ReSharper disable once StaticMemberInGenericType

        // SUPPRESSED ON PURPOSE (:
#pragma warning disable S2743 // Static fields should not be used in generic types
        // ReSharper disable once StaticMemberInGenericType
        private static readonly Func<PropertyInfo, Type, bool>[] _propertySetterStrategies =
#pragma warning restore S2743 // Static fields should not be used in generic types
        {
            IsNotWritable,
            HaveSetSimpleSetterFor,
            IsEnumType,
            IsCollectionType,
            HaveSetNullableTypeSetterFor,
            SetupBuilderSetterFor
        };

        private static bool IsEnumType(PropertyInfo prop, Type propertyType)
        {
            if (!propertyType.IsEnum)
                return false;
            RandomPropSetters[prop.Name] = (entity, idx) =>
            {
                prop.SetValue(entity, RandomValueGen.GetRandomEnum(propertyType));
            };
            return true;
        }

        private static bool HaveSetSimpleSetterFor(PropertyInfo prop, Type propertyType)
        {
            Func<PropertyInfo, Action<TEntity, int>> setterGenerator;
            if (!_simpleTypeSetters.TryGetValue(propertyType, out setterGenerator))
                return false;
            RandomPropSetters[prop.Name] = setterGenerator(prop);
            return true;
        }

        private static bool SetterResultFor(Func<bool> finalFunc, string failMessage)
        {
            var result = finalFunc();
            if (!result)
                Trace.WriteLine(failMessage);
            return result;
        }

        // TODO: delay this check until we have an instance: the generic builder may
        //  be created against a type which is implemented / overridden by another which
        //  provides write access on the property. I'm specifically thinking about
        //  builders doing funky stuff with interfaces...
        private static bool IsNotWritable(PropertyInfo prop, Type propertyType)
        {
            return SetterResultFor(() => !prop.CanWrite, $"{prop.Name} is not writable");
        }

        private static bool IsNullableType(Type type)
        {
            return type.IsGenericType &&
                    type.GetGenericTypeDefinition() == NullableGeneric;
        }


        private static bool HaveSetNullableTypeSetterFor(PropertyInfo prop, Type propertyType)
        {
            if (!IsNullableType(propertyType))
                return false;
            var underlyingType = Nullable.GetUnderlyingType(propertyType);
            SetSetterForType(prop, underlyingType);
            return true;
        }

        private static bool SetupBuilderSetterFor(PropertyInfo prop, Type propertyType)
        {
            var builderType = TryFindUserBuilderFor(prop.PropertyType)
                              ?? FindOrCreateDynamicBuilderFor(prop);
            if (builderType == null)
                return false;
            RandomPropSetters[prop.Name] = (e, i) =>
            {
                if (TraversedTooManyTurtles(i)) return;
                var dynamicBuilder = Activator.CreateInstance(builderType) as IGenericBuilder;
                if (dynamicBuilder == null)
                    return;
                prop.SetValue(e, dynamicBuilder.WithBuildLevel(i).GenericWithRandomProps().GenericBuild(), null);
            };
            return true;
        }

        private static bool TraversedTooManyTurtles(int i)
        {
            var stackTrace = new StackTrace();
            var frames = stackTrace.GetFrames();
            if (HaveReenteredOwnRandomPropsTooManyTimesFor(frames))
                return true;
            return i > MaxRandomPropsLevel;
        }

        private static bool HaveReenteredOwnRandomPropsTooManyTimesFor(StackFrame[] frames)
        {
            var level = frames.Aggregate(0, (acc, cur) =>
            {
                var thisMethod = cur.GetMethod();
                var thisType = thisMethod.DeclaringType;
                if (thisType != null &&
                        thisType.IsGenericType &&
                        GenericBuilderBaseType.IsAssignableFrom(thisType) &&
                        thisMethod.Name == "SetRandomProps")
                {
                    return acc + 1;
                }
                return acc;
            });
            return level >= MaxRandomPropsLevel;
        }

        private static Type FindOrCreateDynamicBuilderFor(PropertyInfo propInfo)
        {
            Type builderType;
            if (DynamicBuilders.TryGetValue(propInfo.PropertyType, out builderType))
                return builderType;
            try
            {
                return GenerateDynamicBuilderFor(propInfo);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Error defining dynamic builder for property of type: {propInfo.PropertyType.Name}: " + ex.Message);
                return null;
            }
        }

        private static Type GenerateDynamicBuilderFor(PropertyInfo prop)
        {
            return FindOrGenerateDynamicBuilderFor(prop.PropertyType);
        }

        private static Type TryFindUserBuilderFor(Type propertyType)
        {
            Type builderType;
            if (!UserBuilders.TryGetValue(propertyType, out builderType))
            {
                var existingBuilder = GenericBuilderLocator.TryFindExistingBuilderFor(propertyType);
                if (existingBuilder == null) return null;
                UserBuilders[propertyType] = existingBuilder;
                builderType = existingBuilder;
            }
            return builderType;
        }

        private int _buildLevel;
        private void SetRandomProps(TEntity entity)
        {
            foreach (var prop in EntityPropInfo)
            {
                try
                {
                    RandomPropSetters[prop.Name](entity, _buildLevel + 1);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Unable to set random prop: {ex.Message}");
                }
            }
        }
    }

}
