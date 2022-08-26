// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.ComponentModel;
using System.Collections.Generic;

/// <summary>
/// Helper functions for loading AST nodes from XML elements.
/// </summary>
static class XMLHelper
{
    /// <summary>
    /// Gets the given attribute from the XML element and parses it as the
    /// required type. An exception is thrown if the attribute does not exist.
    /// </summary>
    public static T Get<T>(this XElement xelem, string attribute)
    {
        XAttribute a = xelem.Attribute(attribute);
        if (a == null) throw new Exception($"xelement {xelem.Name} didn't have attribute {attribute}");
        return (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(a.Value);
    }

    /// <summary>
    /// Gets the given attribute from the XML element and parses it as the
    /// required type. If the attributed is not present, the provided default
    /// value is returned instead.
    /// </summary>
    public static T Get<T>(this XElement xelem, string attribute, T dflt)
    {
        XAttribute a = xelem.Attribute(attribute);
        return a == null ? dflt : (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(a.Value);
    }
    
    /// <summary>
    /// Returns the line number at which the given XML element occurs in the
    /// source file.
    /// </summary>
    public static int LineNumber(this XElement xelem) => ((System.Xml.IXmlLineInfo)xelem).LineNumber;

    /// <summary>
    /// Returns an enumerable of the children of this XML element which match
    /// one of the provided tag names.
    /// </summary>
    public static IEnumerable<XElement> Elements(this XElement xelement, params string[] names) => xelement.Elements().Where(e => names.Any(n => n == e.Name));
    
    /// <summary>
    /// Returns an enumerable of the descendants of this XML element which
    /// match one of the provided tag names, and which can be reached via only
    /// nodes which also match one of the provided tag names. The descendants
    /// are not necessarily returned in order; this XML element is not
    /// considered a descendant of itself.
    /// </summary>
    public static IEnumerable<XElement> MyDescendants(this XElement xelem, params string[] tags)
    {
        // find matching descendants by breadth-first search (order doesn't matter)
        Queue<XElement> q = new();
        q.Enqueue(xelem);

        while (q.Any())
        {
            XElement e = q.Dequeue();
            if (e != xelem) yield return e;
            foreach (XElement x in e.Elements(tags)) q.Enqueue(x);
        }
    }
}
