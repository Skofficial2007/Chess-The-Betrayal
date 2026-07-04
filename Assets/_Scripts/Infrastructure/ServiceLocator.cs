using System;
using System.Collections.Generic;

namespace ChessTheBetrayal.Infrastructure
{
    /// <summary>
    /// A minimal, project-owned service registry replacing the implicit `public static X Instance`
    /// fan-in pattern. Any of the project's ~96 files could previously reach into GameManager,
    /// UIManager, or BoardVisuals without that dependency being visible anywhere in the Inspector
    /// or in a constructor signature. Registering through here doesn't remove that reach, but it
    /// gives missing registrations a loud failure instead of a silent NullReferenceException three
    /// call-frames deep, and it gives the scene a single, greppable list of what's wired to what
    /// (see Bootstrap).
    ///
    /// Deliberately not a general-purpose DI container: no lifetime scopes, no auto-wiring, no
    /// constructor injection. Register/Resolve is the entire surface — that's the 90%-of-the-benefit,
    /// 10%-of-the-complexity tradeoff for a project this size (see di-migration-plan.md).
    /// </summary>
    public sealed class ServiceLocator
    {
        public static ServiceLocator Instance { get; } = new ServiceLocator();

        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        private ServiceLocator() { }

        public void Register<T>(T service) where T : class
        {
            _services[typeof(T)] = service ?? throw new ArgumentNullException(nameof(service));
        }

        public T Resolve<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out object service))
            {
                return (T)service;
            }

            throw new InvalidOperationException(
                $"{typeof(T).Name} was never registered — check Bootstrap.");
        }

        public bool TryResolve<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out object raw))
            {
                service = (T)raw;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>
        /// Clears all registrations. Called by Bootstrap on teardown so a scene reload doesn't
        /// resolve stale references left behind by destroyed MonoBehaviours.
        /// </summary>
        public void Clear() => _services.Clear();
    }
}
