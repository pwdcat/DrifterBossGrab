using System;
using System.Collections.Generic;

namespace DrifterBossGrabMod
{
    // Composite class to manage collections of configurable components
    // Implements the Composite pattern for handling initialization and cleanup of multiple components
    public class ConfigurationComposite : IConfigurable
    {
        private readonly List<IConfigurable> _components = new List<IConfigurable>();

        // Add a configurable component to the composite
        public void AddComponent(IConfigurable component)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            _components.Add(component);
        }

        // Remove a configurable component from the composite
        public bool RemoveComponent(IConfigurable component)
        {
            return _components.Remove(component);
        }

        // Get all components in the composite
        public IReadOnlyCollection<IConfigurable> GetComponents()
        {
            return _components.AsReadOnly();
        }

        // Initialize all configurable components in the composite
        public void Initialize()
        {
            foreach (var component in _components)
            {
                try
                {
                    component.Initialize();
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to initialize component {component.GetType().Name}: {ex.Message}");
                }
            }
        }

        // Cleanup all configurable components in the composite
        public void Cleanup()
        {
            foreach (var component in _components)
            {
                try
                {
                    component.Cleanup();
                }
                catch (Exception ex)
                {
                    Log.Error($"Failed to cleanup component {component.GetType().Name}: {ex.Message}");
                }
            }

            _components.Clear();
        }
    }
}
