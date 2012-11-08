open System
open System.Data
open System.Data.Linq
open System.IO
open System.Linq
open System.Xml
open System.Xml.Schema
open Microsoft.FSharp.Data.TypeProviders
open Microsoft.FSharp.Linq

let folder = @"E:\StackExchangeDump";

let parseInt = System.Int32.TryParse >> function
    | true, v -> v
    | false, _ -> 0
 
type Answer = { Id: int; UserId: int; Score: int; ParentId: int }
type Question = { Id: int; UserId: int; Score: int; Answers: Answer list }
type User = { UserId: int; Reputation: int }

let readQuestion (reader: XmlReader) = 
    { Id = parseInt(reader.["Id"]); UserId = parseInt(reader.["UserId"]); Score = parseInt(reader.["Score"]); Answers = List.empty }

let readAnswer (reader: XmlReader) : Answer = 
    { Id = parseInt(reader.["Id"]); UserId = parseInt(reader.["UserId"]); Score = parseInt(reader.["Score"]); ParentId = parseInt(reader.["ParentId"]) }

let readUser (reader: XmlReader) : User option = 
    if reader.Read() then
        Some ( { UserId = parseInt(reader.["Id"]); Reputation = parseInt(reader.["Reputation"]) } )
    else
        None

let readPostRow (reader: XmlReader) (qs: Question list, anss: Answer list) = 
    if reader.Read() then
        match reader.["PostTypeId"] with
            | "1" -> ( readQuestion reader :: qs, anss)
            | "2" -> (qs, readAnswer reader :: anss )
            | unk -> printfn "Unexpected PostTypeId %s" unk; (qs, anss )
    else
        (qs, anss) // ignore the row if unreadable

let log(i, itemName) = 
    if (i % 100000 = 0) then 
        printf "."
    if (i % 1000000 = 0) then 
        printfn "Read %d %s" i itemName

let logIteration (seq, itemName) =
    seq |> Seq.mapi(fun i x -> log(i + 1, itemName); x)

let readAllRows (reader : XmlReader) = 
    // Files are huge; let's take just a few sample records for debugging
    if System.Diagnostics.Debugger.IsAttached then    
        seq { 
            for i in 1..100 do 
                reader.ReadStartElement("row")
                yield reader
        }
    else 
        seq {
            while reader.IsStartElement("row") do
                reader.ReadStartElement("row")
                yield reader
        }

let writeUser (writer: XmlWriter) user =
    match user with 
        | Some u -> 
            writer.WriteStartElement("node")
            writer.WriteAttributeString("id", u.UserId.ToString())
            if u.Reputation <> 0 then // 0 is default value; saves space in file to not write is out since huge number of users with rep = 0
                writer.WriteStartElement("data")
                writer.WriteAttributeString("key", "rep")
                writer.WriteValue(u.Reputation)
                writer.WriteEndElement()
            writer.WriteEndElement()
        | None -> printfn "Bad user record found. Ignoring."

let importUsers(writer: User option -> unit) = 
    use inFile = new FileStream(Path.Combine(folder, "users.xml"), FileMode.Open, FileAccess.Read)
    use reader = XmlReader.Create(inFile)
    reader.MoveToElement() |> ignore
    reader.ReadStartElement("users")
    logIteration(readAllRows reader, "users") |> Seq.iter(fun reader -> readUser(reader) |> writer)

let importPosts () = 
    use inFile = new FileStream(Path.Combine(folder, "posts.xml"), FileMode.Open, FileAccess.Read)
    use reader = XmlReader.Create(inFile)
    reader.MoveToElement() |> ignore
    reader.ReadStartElement("posts")
    let z: Question list * Answer list = (List.empty, List.Empty)
    let (qs, anss) = logIteration(readAllRows reader, "posts") |> Seq.fold(fun accum elem -> readPostRow elem accum) z 
    let la = List.length(anss)
    let lq = List.length(qs)
    printfn "Read %d questions and %d answers" lq la

let writeGraphML () = 
    use outFile = new FileStream(Path.Combine(folder, "StackOverflow.gml"), FileMode.Create, FileAccess.Write)
    let settings = new XmlWriterSettings()
    if System.Diagnostics.Debugger.IsAttached then
        settings.Indent <- true 
    use writer = XmlWriter.Create(outFile, settings)
    // Necessary GraphML headers
    writer.WriteStartElement("graphml", "http://graphml.graphdrawing.org/xmlns")
    writer.WriteAttributeString("xmlns", "xsi", null, XmlSchema.InstanceNamespace)
    writer.WriteAttributeString("xsi", "schemaLocation", null, "http://graphml.graphdrawing.org/xmlns http://graphml.graphdrawing.org/xmlns/1.0/graphml.xsd")
    // Custom GraphML-Attributes
    writer.WriteStartElement("key")
    writer.WriteAttributeString("id", "rep")
    writer.WriteAttributeString("for", "node")
    writer.WriteAttributeString("attr.name", "reputation")
    writer.WriteAttributeString("attr.type", "int")
    writer.WriteElementString("default", "0")
    writer.WriteEndElement()
    // Graph element
    writer.WriteStartElement("graph")
    writer.WriteAttributeString("id", "G")
    writer.WriteAttributeString("edgedefault", "directed")
    // Nodes
    printfn "Reading users.xml"
    importUsers(writeUser(writer))
    // Edges
    printfn "Reading posts.xml"
    importPosts()
    // Finish document
    writer.WriteEndElement()
    writer.WriteEndElement()
    writer.Flush()
    writer.Close()

[<EntryPoint>]
let main argv = 
    printfn "Started import at %A." System.DateTime.Now
    writeGraphML()
    printfn "Finished import at %A." System.DateTime.Now
    Console.ReadLine() |> ignore
    0
