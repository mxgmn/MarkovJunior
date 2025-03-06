// Copyright (C) 2022 Maxim Gumin, The MIT License (MIT)
using System;
using System.Linq;
using System.Xml.Linq;
using System.ComponentModel;
using System.Collections.Generic;
static class XMLHelper
{
    // Gets an attribute from an XML element and converts it to the specified type
    // Throws an exception if the attribute doesn't exist
    public static T Get<T>(this XElement xelem, string attribute)
    {
        XAttribute a = xelem.Attribute(attribute);
        if (a == null) throw new Exception($"xelement {xelem.Name} didn't have attribute {attribute}");
        // Uses TypeDescriptor to convert the string value to the requested type
        return (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(a.Value);
    }

    // Gets an attribute from an XML element and converts it to the specified type
    // Returns a default value if the attribute doesn't exist
    public static T Get<T>(this XElement xelem, string attribute, T dflt)
    {
        XAttribute a = xelem.Attribute(attribute);
        return a == null ? dflt : (T)TypeDescriptor.GetConverter(typeof(T)).ConvertFromInvariantString(a.Value);
    }

    // Gets the line number of an XML element in the source file
    // Useful for error reporting
    public static int LineNumber(this XElement xelem) => ((System.Xml.IXmlLineInfo)xelem).LineNumber;

    // Gets child elements with specified names
    // Acts as a filter on the Elements() collection
    public static IEnumerable<XElement> Elements(this XElement xelement, params string[] names) =>
        xelement.Elements().Where(e => names.Any(n => n == e.Name));

    // Gets all descendant elements with specified tags using breadth-first search
    // Unlike the standard Descendants() method, this doesn't include the starting element
    public static IEnumerable<XElement> MyDescendants(this XElement xelem, params string[] tags)
    {
        Queue<XElement> q = new();
        q.Enqueue(xelem);
        while (q.Any())
        {
            XElement e = q.Dequeue();
            if (e != xelem) yield return e;  // Skip the root element
            foreach (XElement x in e.Elements(tags)) q.Enqueue(x);  // Only enqueue elements with matching tags
        }
    }
}

/*
========== SUMMARY ==========

This code provides helper methods for working with XML in C#. Think of it as a toolkit that makes reading XML configuration files easier and safer.

In simple terms, here's what the XMLHelper class does:

1. Type-Safe Attribute Reading:
   - The two `Get<T>` methods read attributes from XML elements and automatically convert them to the right type
   - The first version throws an error if the attribute is missing
   - The second version returns a default value if the attribute is missing
   
   This is like having a smart assistant that not only finds information in a document but also translates it to exactly the format you need.

2. Error Tracking:
   - The `LineNumber` method helps identify where in the XML file an element was defined
   - This makes error messages more helpful by pinpointing the exact line where a problem occurs
   
   This is similar to having page numbers in a book - when something goes wrong, you know exactly where to look.

3. Element Selection:
   - The `Elements` method finds child elements with specific names
   - The `MyDescendants` method searches for elements with specific tags throughout the entire tree structure
   
   This is like having a search function that can either look just in the current chapter or throughout the entire book.

These helpers are used throughout the procedural generation system to load configuration data from XML files. They make the code more concise and error-resistant by handling type conversion and providing clear error messages when something goes wrong.
*/