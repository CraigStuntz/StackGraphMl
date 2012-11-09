open System
open System.Data
open System.Data.Linq
open System.IO
open System.Linq
open System.Xml
open System.Xml.Schema
open Microsoft.FSharp.Data.TypeProviders
open Microsoft.FSharp.Linq

let folder = @"C:\Users\craig\Documents\Visual Studio 2012\Projects\StackGraphMl\StackGraphML\" // @"E:\StackExchangeDump";

let parseInt = System.Int32.TryParse >> function
    | true, v -> v
    | false, _ -> 0
 
type Answer   = { Id: int; UserId: int; Score: int; ParentId: int }
type Question = { Id: int; UserId: int; Score: int }
type User     = { UserId: int; Reputation: int }

let readQuestion (reader: XmlReader) = 
    { Id = parseInt(reader.["Id"]); UserId = parseInt(reader.["OwnerUserId"]); Score = parseInt(reader.["Score"]) }

let readAnswer (reader: XmlReader) : Answer = 
    { Id = parseInt(reader.["Id"]); UserId = parseInt(reader.["OwnerUserId"]); Score = parseInt(reader.["Score"]); ParentId = parseInt(reader.["ParentId"]) }

let readUser (reader: XmlReader) : User option = 
    if reader.Read() then
        Some ( { UserId = parseInt(reader.["Id"]); Reputation = parseInt(reader.["Reputation"]) } )
    else
        None

let readPostRow (reader: XmlReader) (qs: Question list, anss: Answer list) = 
    if reader.Read() then
        if (parseInt(reader.["Score"]) >= 0) && (System.String.IsNullOrEmpty(reader.["ClosedDate"]) )then
            match reader.["PostTypeId"] with
                | "1" -> ( readQuestion reader :: qs, anss)
                | "2" -> (qs, readAnswer reader :: anss )
                | unk -> (qs, anss) // Ignore wikis, election stuff, etc.
        else
            (qs, anss) // exclude severely down-voted or closed posts
    else
        printfn "Found bad post. Ignoring"
        (qs, anss)

let log i = 
    if (i % 100000 = 0) then 
        printf "."
    if (i % 1000000 = 0) then 
        printfn "%d" i

let logIteration (seq) =
    seq |> Seq.mapi(fun i x -> log(i + 1); x)

let readAllRows (reader : XmlReader) = 
    // Files are huge; let's take just a few sample records for debugging
    if System.Diagnostics.Debugger.IsAttached then    
        seq { 
            for i in 1..8 do 
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
    writer.WriteStartElement("node")
    writer.WriteAttributeString("id", user.UserId.ToString())
    if user.Reputation <> 0 then // 0 is default value; saves space in file to not write is out since huge number of users with rep = 0
        writer.WriteStartElement("data")
        writer.WriteAttributeString("key", "r")
        writer.WriteValue(user.Reputation)
        writer.WriteEndElement()
    writer.WriteEndElement()

let importUsers(writer: User -> unit) = 
    use inFile = new FileStream(Path.Combine(folder, "users.xml"), FileMode.Open, FileAccess.Read)
    use reader = XmlReader.Create(inFile)
    reader.MoveToElement() |> ignore
    reader.ReadStartElement("users")
    let users = 
        logIteration(readAllRows reader) 
        |> Seq.choose(fun reader -> readUser(reader))
        |> List.ofSeq
    users |> List.iter(fun user -> writer(user))
    printfn "Read %d users" (List.length(users))

let writePost (writer: XmlWriter) (userId: int, answerUserId: int, score: int) = 
    writer.WriteStartElement("edge")
    writer.WriteAttributeString("source", userId.ToString())
    writer.WriteAttributeString("target", answerUserId.ToString())
    if score > 0 then
        writer.WriteStartElement("data")
        writer.WriteAttributeString("key", "w")
        writer.WriteValue(score)
        writer.WriteEndElement()
    writer.WriteEndElement()    

let importPosts (writer: XmlWriter) = 
    use inFile = new FileStream(Path.Combine(folder, "posts.xml"), FileMode.Open, FileAccess.Read)
    use reader = XmlReader.Create(inFile)
    reader.MoveToElement() |> ignore
    reader.ReadStartElement("posts")
    let z: Question list * Answer list = (List.empty, List.Empty)
    let (qs, anss) = logIteration(readAllRows reader) |> Seq.fold(fun accum elem -> readPostRow elem accum) z 
    let la = List.length(anss)
    let lq = List.length(qs)
    printfn "Read %d questions and %d answers. Calcluating edges. Grouping answers." lq la
    let answersByParentId = 
        Seq.ofList(anss) 
        |> Seq.groupBy(fun a -> a.ParentId) 
        |> Map.ofSeq
    printfn "Matching users."
    let edgesFor question = 
        match answersByParentId.TryFind(question.Id) with
            | Some answers -> 
                answers 
                |> Seq.map(fun answer -> (question.UserId, answer.UserId, question.Score + answer.Score)) 
            | _ -> Seq.empty
    let questionUserIdWithAnswerUserIds = Async.Parallel [ for q in qs -> async { return edgesFor(q) } ] |> Async.RunSynchronously 
    printfn "Writing edges."
    let edges = questionUserIdWithAnswerUserIds |> Seq.collect(fun edges -> edges) 
    edges |> Seq.iter(writePost(writer))
    printfn "Wrote %d edges." (edges.Count())

let writeGraphML () = 
    use outFile = new FileStream(Path.Combine(folder, "StackOverflow.graphml"), FileMode.Create, FileAccess.Write)
    let settings = new XmlWriterSettings()
    if System.Diagnostics.Debugger.IsAttached then
        settings.Indent <- true 
    use writer = XmlWriter.Create(outFile, settings)
    // Necessary GraphML headers
    writer.WriteStartElement("graphml", "http://graphml.graphdrawing.org/xmlns")
    writer.WriteAttributeString("xmlns", "xsi", null, XmlSchema.InstanceNamespace)
    writer.WriteAttributeString("xsi", "schemaLocation", null, "http://graphml.graphdrawing.org/xmlns http://graphml.graphdrawing.org/xmlns/1.0/graphml.xsd")
    // Custom GraphML-Attributes
    // reputation
    writer.WriteStartElement("key")
    writer.WriteAttributeString("id", "r")
    writer.WriteAttributeString("for", "node")
    writer.WriteAttributeString("attr.name", "reputation")
    writer.WriteAttributeString("attr.type", "int")
    writer.WriteElementString("default", "0")
    writer.WriteEndElement()
    // edge weight
    writer.WriteStartElement("key")
    writer.WriteAttributeString("id", "w")
    writer.WriteAttributeString("for", "edge")
    writer.WriteAttributeString("attr.name", "weight")
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
    importPosts(writer)
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
    if System.Diagnostics.Debugger.IsAttached then
        Console.ReadLine() |> ignore
    0
