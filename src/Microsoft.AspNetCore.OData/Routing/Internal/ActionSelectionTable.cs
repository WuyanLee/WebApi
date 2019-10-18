// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
#if NETSTANDARD2_0
#else
using Microsoft.AspNetCore.Http;
#endif
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNet.OData.Routing.Internal
{
    internal class ActionSelectionTable<TItem>
    {
        private ActionSelectionTable(
            int version,
            string[] routeKeys,
            Dictionary<string[], List<TItem>> ordinalEntries,
            Dictionary<string[], List<TItem>> ordinalIgnoreCaseEntries)
        {
            Version = version;
            RouteKeys = routeKeys;
            OrdinalEntries = ordinalEntries;
            OrdinalIgnoreCaseEntries = ordinalIgnoreCaseEntries;
        }

        public int Version { get; }

        private string[] RouteKeys { get; }

        private Dictionary<string[], List<TItem>> OrdinalEntries { get; }

        private Dictionary<string[], List<TItem>> OrdinalIgnoreCaseEntries { get; }

        public static ActionSelectionTable<ActionDescriptor> Create(ActionDescriptorCollection actions)
        {
            return CreateCore<ActionDescriptor>(

                // We need to store the version so the cache can be invalidated if the actions change.
                version: actions.Version,

                // For action selection, ignore attribute routed actions
                items: actions.Items.Where(a => a.AttributeRouteInfo == null),

                getRouteKeys: a => a.RouteValues?.Keys,
                getRouteValue: (a, key) =>
                {
                    string value = null;
                    a.RouteValues?.TryGetValue(key, out value);
                    return value ?? string.Empty;
                });
        }


        private static ActionSelectionTable<T> CreateCore<T>(
            int version,
            IEnumerable<T> items,
            Func<T, IEnumerable<string>> getRouteKeys,
            Func<T, string, string> getRouteValue)
        {
            // We need to build two maps for all of the route values.
            var ordinalEntries = new Dictionary<string[], List<T>>(StringArrayComparer.Ordinal);
            var ordinalIgnoreCaseEntries = new Dictionary<string[], List<T>>(StringArrayComparer.OrdinalIgnoreCase);

            // We need to hold on to an ordered set of keys for the route values. We'll use these later to
            // extract the set of route values from an incoming request to compare against our maps of known
            // route values.
            var routeKeys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                var keys = getRouteKeys(item);
                if (keys != null)
                {
                    foreach (var key in keys)
                    {
                        routeKeys.Add(key);
                    }
                }
            }

            foreach (var item in items)
            {
                // This is a conventionally routed action - so we need to extract the route values associated
                // with this action (in order) so we can store them in our dictionaries.
                var index = 0;
                var routeValues = new string[routeKeys.Count];
                foreach (var key in routeKeys)
                {
                    var value = getRouteValue(item, key);
                    routeValues[index++] = value;
                }

                if (!ordinalIgnoreCaseEntries.TryGetValue(routeValues, out var entries))
                {
                    entries = new List<T>();
                    ordinalIgnoreCaseEntries.Add(routeValues, entries);
                }

                entries.Add(item);

                // We also want to add the same (as in reference equality) list of actions to the ordinal entries.
                // We'll keep updating `entries` to include all of the actions in the same equivalence class -
                // meaning, all conventionally routed actions for which the route values are equal ignoring case.
                //
                // `entries` will appear in `OrdinalIgnoreCaseEntries` exactly once and in `OrdinalEntries` once
                // for each variation of casing that we've seen.
                if (!ordinalEntries.ContainsKey(routeValues))
                {
                    ordinalEntries.Add(routeValues, entries);
                }
            }

            return new ActionSelectionTable<T>(version, routeKeys.ToArray(), ordinalEntries, ordinalIgnoreCaseEntries);
        }

        public IReadOnlyList<TItem> Select(RouteValueDictionary values)
        {
            // Select works based on a string[] of the route values in a pre-calculated order. This code extracts
            // those values in the correct order.
            var routeKeys = RouteKeys;
            var routeValues = new string[routeKeys.Length];
            for (var i = 0; i < routeKeys.Length; i++)
            {
                values.TryGetValue(routeKeys[i], out var value);
                routeValues[i] = value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }

            // Now look up, first case-sensitive, then case-insensitive.
            if (OrdinalEntries.TryGetValue(routeValues, out var matches) ||
                OrdinalIgnoreCaseEntries.TryGetValue(routeValues, out matches))
            {
                Debug.Assert(matches != null);
                Debug.Assert(matches.Count >= 0);
                return matches;
            }

            return Array.Empty<TItem>();
        }
    }
}

