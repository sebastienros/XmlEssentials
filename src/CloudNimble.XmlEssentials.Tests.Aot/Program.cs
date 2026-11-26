using System;
using System.Diagnostics.CodeAnalysis;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.Serialization;
using CloudNimble.XmlEssentials.Tests.Aot.Generated;

namespace CloudNimble.XmlEssentials.Tests.Aot
{

    /// <summary>
    /// Native AOT smoke: generated context, XSD-generated types, reflection off, reader walk.
    /// </summary>
    /// <remarks>
    /// Publish with <c>dotnet publish -c Release -p:PublishAot=true</c> and run the native executable.
    /// Exit code 0 means every check matched. Exit code 1 prints the first mismatch.
    /// </remarks>
    public static class Program
    {

        #region Public Methods

        /// <summary>
        /// Runs the AOT smoke checks.
        /// </summary>
        /// <param name="args">Unused.</param>
        /// <returns>0 on success, 1 on failure.</returns>
        public static int Main(string[] args)
        {
            _ = args;
            string personXml;
            string orderXml;
            string inventoryXml;
            try
            {
                CheckReflectionIsOff();
                personXml = CheckPersonRoundTrip();
                orderXml = CheckOrderFromXsd();
                CheckShapesFromXsd();
                inventoryXml = CheckInventoryRoundTrip();
                EngineFeatures.Check();
                XPathFeatures.Check();
                CheckTrimmedShapes();
                CheckReader();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            Console.WriteLine(personXml);
            Console.WriteLine(orderXml);
            Console.WriteLine(inventoryXml);
            Console.WriteLine("XmlEssentials AOT smoke passed.");
            return 0;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Fails when <paramref name="actual"/> is not <paramref name="expected"/>.
        /// </summary>
        /// <param name="actual">The value produced.</param>
        /// <param name="expected">The required value.</param>
        /// <param name="name">The check name.</param>
        internal static void CheckEqual(string actual, string expected, string name)
        {
            if (actual == expected)
            {
                return;
            }

            throw new InvalidOperationException(name + ":" + Environment.NewLine + "expected: " + expected + Environment.NewLine + "actual:   " + actual);
        }

        /// <summary>
        /// Serializes a type whose members the runtime library reads and writes from generated metadata, and reads it back.
        /// </summary>
        /// <returns>The serialized document.</returns>
        /// <remarks>
        /// A dictionary, collections that are not lists, a collection with no setter, an XML value, and a value
        /// declared as object are read and written by the runtime library, not by generated code. This check
        /// proves they work with reflection off and the code trimmed.
        /// </remarks>
        internal static string CheckInventoryRoundTrip()
        {
            const string expected = "<Inventory><Any>5</Any><Codes>1</Codes><Codes>2</Codes><Counts><bolts>40</bolts></Counts><Notes>n</Notes>"
                + "<Pending>7</Pending><Pending>8</Pending><Raw><part id=\"9\"/></Raw><Sizes>3</Sizes><Tags>new</Tags></Inventory>";
            using var document = XmlDocument.Parse("<part id=\"9\"/>");
            var inventory = new Inventory
            {
                Any = 5,
                Codes = [1, 2],
                Counts = { ["bolts"] = 40 },
                Pending = new System.Collections.Generic.Queue<int>([7, 8]),
                Raw = document.RootElement,
                Sizes = [3],
                Tags = ["new"],
            };
            inventory.Notes.Add("n");
            var xml = XmlSerializer.Serialize(inventory, AppXmlContext.Default.Inventory);
            CheckEqual(xml, expected, "inventory");
            var read = XmlSerializer.Deserialize(xml, AppXmlContext.Default.Inventory);
            if (read is null)
            {
                throw new InvalidOperationException("inventory deserialize returned null.");
            }

            CheckEqual(read.Counts["bolts"].ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + read.Pending.Count + "," + read.Sizes.Length + "," + read.Notes.Count + "," + read.Tags.Count + "," + read.Codes.Length, "40,2,1,1,1,2", "inventory-values");
            CheckEqual(((XmlElement)read.Any!).GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture), "5", "inventory-any");
            read.Any = 5;
            CheckEqual(XmlSerializer.Serialize(read, AppXmlContext.Default.Inventory), expected, "inventory-roundtrip");
            return xml;
        }

        /// <summary>
        /// Writes and reads the member shapes whose collection type, item type or generic derived type the
        /// serializer once found by reflection, and that a generated context now names.
        /// </summary>
        /// <remarks>
        /// The expected documents are the ones the reflection resolver writes for the same values. Here there is
        /// no reflection resolver: the interfaces of the collection types are not read, no generic type is closed
        /// at run time, and the constructors and the <c>Add</c> method are kept because the context names the types.
        /// </remarks>
        internal static void CheckTrimmedShapes()
        {
            const string expected =
                "<TrimmedShapes><Bag>4</Bag><Bag>5</Bag><ShapeFull><Value>9</Value></ShapeFull><Fit>Loose</Fit>" +
                "<ShapeDisc><R>3</R></ShapeDisc><ShapeTile><Side>2</Side></ShapeTile>" +
                "<NoFit xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"/><Points><X>1</X><Y>2</Y></Points>" +
                "<Rates><a>1.5</a><b>2.5</b></Rates><Set>s</Set><Stack>2</Stack><Stack>1</Stack></TrimmedShapes>";
            var shapes = new TrimmedShapes { Fit = ShapeFit.Loose, Box = new ShapeFull<int> { Value = 9 } };
            shapes.Bag.Add(4);
            shapes.Bag.Add(5);
            shapes.Kinds.Add(new ShapeDisc { R = 3 });
            shapes.Kinds.Add(new ShapeTile { Side = 2 });
            shapes.Points.Add(new ShapePoint { X = 1, Y = 2 });
            shapes.Rates["b"] = 2.5;
            shapes.Rates["a"] = 1.5;
            shapes.Set.Add("s");
            shapes.Stack.Push(1);
            shapes.Stack.Push(2);
            var xml = XmlSerializer.Serialize(shapes, TrimmedXmlContext.Default.TrimmedShapes);
            CheckEqual(xml, expected, "trimmed-shapes");
            var read = XmlSerializer.Deserialize(xml, TrimmedXmlContext.Default.TrimmedShapes);
            if (read is null)
            {
                throw new InvalidOperationException("trimmed-shapes deserialize returned null.");
            }

            var rates = new System.Text.StringBuilder();
            foreach (var rate in read.Rates)
            {
                rates.Append(rate.Key).Append('=').Append(rate.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }

            CheckEqual(
                string.Join(",", read.Bag) + "|" + (read.Box as ShapeFull<int>)?.Value + "|" + read.Fit + "|" + (read.Kinds[0] as ShapeDisc)?.R + "," + (read.Kinds[1] as ShapeTile)?.Side + "|" + (read.NoFit is null) + "|" + read.Points[0].X + "," + read.Points[0].Y
                    + "|" + rates + "|" + string.Join(",", read.Set) + "|" + string.Join(",", read.Stack),
                "4,5|9|Loose|3,2|True|1,2|a=1.5;b=2.5;|s|2,1",
                "trimmed-shapes-values");
            CheckEqual(XmlSerializer.Serialize(read, TrimmedXmlContext.Default.TrimmedShapes), expected, "trimmed-shapes-roundtrip");

            const string paraXml = "<TrimmedPara>a <b/> c</TrimmedPara>";
            var para = new TrimmedPara
            {
                Content = [new XmlMixedText("a "), new XmlMixedElement(new CloudNimble.Text.Xml.Nodes.XmlElementNode("b")), new XmlMixedText(" c")],
            };
            CheckEqual(XmlSerializer.Serialize(para, TrimmedXmlContext.Default.TrimmedPara), paraXml, "trimmed-para");
            var readPara = XmlSerializer.Deserialize(paraXml, TrimmedXmlContext.Default.TrimmedPara);
            CheckEqual((readPara?.Content.Length ?? -1).ToString(System.Globalization.CultureInfo.InvariantCulture), "3", "trimmed-para-items");
        }

        /// <summary>
        /// Serializes an XSD-generated order through its generated context and round-trips the same document.
        /// </summary>
        /// <returns>The serialized document.</returns>
        internal static string CheckOrderFromXsd()
        {
            const string expected = "<Order xmlns=\"urn:order\" currency=\"USD\"><Id>A-1</Id><Status>Open</Status><Qty>2</Qty><Line><Sku>sku</Sku><Qty>1</Qty></Line></Order>";
            var options = new XmlSerializerOptions { OmitXmlDeclaration = true, DefaultNamespace = "urn:order" };
            var context = new OrderXmlContext(options);
            var order = new Order
            {
                currency = "USD",
                Id = "A-1",
                Status = Status.Open,
                Qty = 2,
                Line = [new Order_Line { Sku = "sku", Qty = 1 }],
            };
            var xml = XmlSerializer.Serialize(order, context.Order);
            CheckEqual(xml, expected, "xsd-order");
            var read = XmlSerializer.Deserialize(xml, context.Order);
            if (read is null)
            {
                throw new InvalidOperationException("xsd-order deserialize returned null.");
            }

            CheckEqual(XmlSerializer.Serialize(read, context.Order), expected, "xsd-order-roundtrip");
            return xml;
        }

        /// <summary>
        /// Reads a document for the XSD-generated shapes, whose types derive from each other, and writes it back.
        /// </summary>
        /// <remarks>
        /// The document names the derived type with a prefix in <c>xsi:type</c>, as a document written against the
        /// schema by another producer does. The optional elements it leaves out are left out again, the attribute
        /// it leaves out has its default, the choice of two text elements says which one it was, and the choice that
        /// repeats keeps its order, mixed content included.
        /// </remarks>
        internal static void CheckShapesFromXsd()
        {
            const string document =
                "<d:drawing xmlns:d=\"urn:shapes\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" scale=\"1.5\">" +
                "<d:Main xsi:type=\"d:Ring\" id=\"1\"><d:Name>m</d:Name><d:Radius>3</d:Radius></d:Main>" +
                "<d:Mode>next-day</d:Mode><d:Tags><d:Tag>a</d:Tag><d:Tag>b</d:Tag></d:Tags>" +
                "<d:Work>w</d:Work><d:Text>t</d:Text><d:Body>one <d:b>two</d:b></d:Body><d:Count>5</d:Count></d:drawing>";
            const string expected =
                "<drawing xmlns=\"urn:shapes\" scale=\"1.5\" rev=\"3\">" +
                "<Main xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:type=\"Ring\" id=\"1\"><Name>m</Name><Radius>3</Radius></Main>" +
                "<Mode>next-day</Mode><Tags><Tag>a</Tag><Tag>b</Tag></Tags>" +
                "<Work>w</Work><Text>t</Text><Body>one <d:b xmlns:d=\"urn:shapes\">two</d:b></Body><Count>5</Count></drawing>";
            var context = new ShapesXmlContext(new XmlSerializerOptions { OmitXmlDeclaration = true, DefaultNamespace = "urn:shapes" });
            var read = XmlSerializer.Deserialize(document, context.Drawing);
            if (read?.Main is not Ring { id: 1, Name: "m", Radius: 3, Inner: null }
                || read.Mode is not Mode.next_day
                || read.Note is not null
                || read.Tags is not ["a", "b"]
                || read.rev != 3
                || read.Item is not { Name: "Work", Value: "w" }
                || read.Items is not ["t", Para { Content: [XmlMixedText { Text: "one " }, XmlMixedElement] }, 5])
            {
                throw new InvalidOperationException("xsd-shapes was not read as the document gives it.");
            }

            CheckEqual(XmlSerializer.Serialize(read, context.Drawing), expected, "xsd-shapes-roundtrip");
        }

        /// <summary>
        /// Serializes a source-generated person through <see cref="AppXmlContext"/> and round-trips the same document.
        /// </summary>
        /// <returns>The serialized document.</returns>
        internal static string CheckPersonRoundTrip()
        {
            const string expected = "<Person><Age>36</Age><Name>Ada</Name></Person>";
            var xml = XmlSerializer.Serialize(new Person { Name = "Ada", Age = 36 }, AppXmlContext.Default.Person);
            CheckEqual(xml, expected, "person");
            var read = XmlSerializer.Deserialize(xml, AppXmlContext.Default.Person);
            if (read is null)
            {
                throw new InvalidOperationException("person deserialize returned null.");
            }

            CheckEqual(XmlSerializer.Serialize(read, AppXmlContext.Default.Person), expected, "person-roundtrip");
            return xml;
        }

        /// <summary>
        /// Walks a tiny document with <see cref="Utf8XmlReader"/>.
        /// </summary>
        internal static void CheckReader()
        {
            var reader = new Utf8XmlReader("<root id=\"1\"><n>42</n></root>"u8);
            var starts = 0;
            while (reader.Read())
            {
                if (reader.TokenType is XmlTokenType.StartElement)
                {
                    starts++;
                }
            }

            if (starts != 2)
            {
                throw new InvalidOperationException("reader start-element count: " + starts);
            }
        }

        /// <summary>
        /// Confirms the feature switch is off and an unregistered type cannot serialize.
        /// </summary>
        /// <remarks>
        /// Calls the reflection <c>Serialize</c> overload on purpose so the smoke proves it throws when the switch is off.
        /// </remarks>
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "This check calls the reflection Serialize overload to prove it throws.")]
        [UnconditionalSuppressMessage("Trim", "IL2026", Justification = "This check calls the reflection Serialize overload to prove it throws.")]
        internal static void CheckReflectionIsOff()
        {
            if (XmlSerializer.IsReflectionEnabledByDefault)
            {
                throw new InvalidOperationException("IsReflectionEnabledByDefault must be false in the AOT smoke.");
            }

            // A generated context does not map a type it does not list: it has no answer for one.
            if (AppXmlContext.Default.GetTypeInfo(typeof(Unregistered)) is not null)
            {
                throw new InvalidOperationException("A generated context must not answer for a type it does not list.");
            }

            try
            {
                XmlSerializer.Serialize(new Unregistered { N = 1 });
            }
            catch (InvalidOperationException)
            {
                return;
            }

            throw new InvalidOperationException("Serialize of an unregistered type must throw when reflection is disabled.");
        }

        #endregion

    }

}
