﻿using System;
using System.Collections.Generic;
using System.Linq;
using AW2.Helpers.Collections;

namespace AW2.Game.Arenas
{
    /// <summary>
    /// A collection of gobs in an arena. The collection reflects a list of arena layers.
    /// If the arena layer contents change, the <see cref="GobCollection"/> changes
    /// and vice versa, operations on the <see cref="GobCollection"/> are forwarded
    /// to the underlying arena layers.
    /// </summary>
    public class GobCollection : IEnumerable<Gob>, IObservableCollection<object, Gob>
    {
        /// <summary>
        /// Number of simultaneous iterations over the collection.
        /// </summary>
        private int _isEnumerating;

        /// <summary>
        /// Gobs that were scheduled for removal while enumeration was in progress.
        /// </summary>
        private List<Gob> _removedGobs = new List<Gob>();

        /// <summary>
        /// Gobs that were scheduled for addition while enumeration was in progress.
        /// </summary>
        private List<Gob> _addedGobs = new List<Gob>();

        /// <summary>
        /// The arena layers that contain the gobs.
        /// </summary>
        private IList<ArenaLayer> ArenaLayers { get; set; }

        /// <summary>
        /// The arena layer where the gameplay takes place.
        /// </summary>
        /// <seealso cref="ArenaLayers"/>
        public ArenaLayer GameplayLayer { get; set; }

        /// <summary>
        /// The arena layer right behind the gameplay layer.
        /// </summary>
        /// <seealso cref="GameplayLayer"/>
        public ArenaLayer GameplayBackLayer { get; set; }

        /// <summary>
        /// Creates a new gob collection that reflects the gobs on some arena layers.
        /// </summary>
        public GobCollection(IList<ArenaLayer> arenaLayers)
        {
            if (arenaLayers == null) throw new ArgumentNullException("Initialised gob collection with null arena layers");
            ArenaLayers = arenaLayers;
        }

        /// <summary>
        /// Removes items that satisfy a condition.
        /// </summary>
        /// <param name="condition">The condition by which to remove items.</param>
        public void Remove(Predicate<Gob> condition)
        {
            foreach (var layer in ArenaLayers)
                if (_isEnumerating > 0)
                    _removedGobs.AddRange(layer.Gobs.Where(new Func<Gob, bool>(condition)));
                else
                    layer.Gobs.Remove(condition);
        }

        /// <summary>
        /// Removes the first occurrence of a specific element from the collection.
        /// </summary>
        /// <param name="gob">The element to remove.</param>
        /// <param name="force">Remove the element regardless of how 
        /// <see cref="Removing"/> evaluates on the element.</param>
        public bool Remove(Gob gob, bool force)
        {
            if (gob.Layer == null) return false;
            if (Removing != null && !Removing(gob) && !force) return false;
            if (_isEnumerating > 0)
            {
                _removedGobs.Add(gob);
                return gob.Layer.Gobs.Contains(gob);
            }
            else
            {
                bool success = gob.Layer.Gobs.Remove(gob);
                if (success && Removed != null) Removed(gob);
                return success;
            }
        }

        /// <summary>
        /// Called before a single item is removed from the collection.
        /// The the event returns <c>false</c> the removal will not proceed
        /// and the item will stay in the collection.
        /// </summary>
        public event Predicate<Gob> Removing;

        #region IObservableCollection<object, Gob> Members

        public event Action<Gob> Added;
        public event Action<Gob> Removed;
        public event Action<IEnumerable<Gob>> Cleared
        {
            add { throw new NotImplementedException("GobCollection.Cleared event is not in use"); }
            remove { throw new NotImplementedException("GobCollection.Cleared event is not in use"); }
        }
        public event Func<object, Gob> NotFound
        {
            add { throw new NotImplementedException("GobCollection.NotFound event is not in use"); }
            remove { throw new NotImplementedException("GobCollection.NotFound event is not in use"); }
        }

        #endregion

        #region ICollection<Gob> Members

        /// <summary>
        /// Adds an item to the collection.
        /// </summary>
        public void Add(Gob gob)
        {
            if (gob.Layer == null)
                gob.Layer = gob.LayerPreference == Gob.LayerPreferenceType.Front
                    ? GameplayLayer
                    : GameplayBackLayer;
            if (_isEnumerating > 0)
                _addedGobs.Add(gob);
            else
            {
                gob.Layer.Gobs.Add(gob);
                if (Added != null) Added(gob);
            }
        }

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public void Clear()
        {
            foreach (var layer in ArenaLayers)
                if (_isEnumerating > 0)
                    _removedGobs.AddRange(layer.Gobs);
                else
                    layer.Gobs.Clear();
        }

        /// <summary>
        /// Determines whether the collection contains a specific value.
        /// </summary>
        public bool Contains(Gob gob)
        {
            return gob.Layer.Gobs.Contains(gob);
        }

        /// <summary>
        /// Copies the elements of the collection to an array, starting at a particular array index.
        /// </summary>
        public void CopyTo(Gob[] array, int arrayIndex)
        {
            foreach (var layer in ArenaLayers)
            {
                layer.Gobs.CopyTo(array, arrayIndex);
                arrayIndex += layer.Gobs.Count;
            }
        }

        /// <summary>
        /// The number of elements contained in the collection.
        /// </summary>
        public int Count { get { return ArenaLayers.Sum(layer => layer.Gobs.Count); } }

        /// <summary>
        /// Is the collection read-only.
        /// </summary>
        public bool IsReadOnly { get { return false; } }

        /// <summary>
        /// Removes the first occurrence of a specific element from the collection.
        /// </summary>
        public bool Remove(Gob gob)
        {
            return Remove(gob, false);
        }

        #endregion

        #region IEnumerable<Gob> Members

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        public IEnumerator<Gob> GetEnumerator()
        {
            try
            {
                ++_isEnumerating;
                foreach (var layer in ArenaLayers)
                    foreach (var gob in layer.Gobs)
                        yield return gob;
            }
            finally
            {
                --_isEnumerating;
                if (_isEnumerating == 0)
                {
                    var oldAddedGobs = _addedGobs;
                    var oldRemovedGobs = _removedGobs;
                    _addedGobs = new List<Gob>();
                    _removedGobs = new List<Gob>();
                    foreach (var gob in oldAddedGobs) Add(gob);
                    foreach (var gob in oldRemovedGobs) Remove(gob, true);
                }
            }
        }

        #endregion

        #region IEnumerable Members

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }
}