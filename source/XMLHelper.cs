// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)

using System;
using System.Linq;
using System.Xml.Linq;
using System.ComponentModel;
using System.Collections.Generic;

static class XMLHelper
{
    public static T Get<T>(this XElement xelem, string attribute)
    {
        XAttribute a = xelem.Attribute(attribute);
        if (a == null) throw new Exception($"xelement {xelem.Name} didn't have attribute {attribute}");
        return (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(a.Value);
    }

    public static T Get<T>(this XElement xelem, string attribute, T dflt)
    {
        XAttribute a = xelem.Attribute(attribute);
        return a == null ? dflt : (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(a.Value);
    }

    public static int LineNumber(this XElement xelem) => ((System.Xml.IXmlLineInfo)xelem).LineNumber;

    public static IEnumerable<XElement> Elements(this XElement xelement, params string[] names) => xelement.Elements().Where(e => names.Any(n => n == e.Name));
    public static IEnumerable<XElement> MyDescendants(this XElement xelem, params string[] tags)
    {
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
