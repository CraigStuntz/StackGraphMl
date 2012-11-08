open System
open System.Data
open System.Data.Linq
open System.IO
open System.Linq
open System.Xml
open System.Xml.Schema
open Microsoft.FSharp.Data.TypeProviders
open Microsoft.FSharp.Linq

let minQuestionVotes = 100 // Smaller min = more questions in result = slower
let folder = @"E:\StackExchangeDump";

type Answer = { Id: int; UserId: int; Score: int; ParentId: int }
type Question = { Id: int; UserId: int; Score: int; Answers: Answer list }
type User = { UserId: int }

let parseInt = System.Int32.TryParse >> function
    | true, v -> v
    | false, _ -> 0
 
let readQuestion (reader: XmlReader) = 
    { Id = parseInt(reader.["Id"]); UserId = parseInt(reader.["UserId"]); Score = parseInt(reader.["Score"]); Answers = List.empty }

let readAnswer (reader: XmlReader) : Answer = 
    { Id = parseInt(reader.["Id"]); UserId = parseInt(reader.["UserId"]); Score = parseInt(reader.["Score"]); ParentId = parseInt(reader.["ParentId"]) }

let readRow (reader: XmlReader) (qs: Question list, anss: Answer list) = 
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
    seq { 
        // while reader.IsStartElement("row") do
        for i in 1..100 do 
            reader.ReadStartElement("row")
            yield reader 
    }

let writeGraphML (users: User seq) = 
    use outFile = new FileStream(Path.Combine(folder, "StackOverflow.gml"), FileMode.Create, FileAccess.Write)
    let settings = new XmlWriterSettings()
    settings.Indent <- true 
    use writer = XmlWriter.Create(outFile, settings)
    writer.WriteStartElement("graphml", "http://graphml.graphdrawing.org/xmlns")
    writer.WriteAttributeString("xmlns", "xsi", null, XmlSchema.InstanceNamespace)
    writer.WriteAttributeString("xsi", "schemaLocation", null, "http://graphml.graphdrawing.org/xmlns http://graphml.graphdrawing.org/xmlns/1.0/graphml.xsd")
    writer.WriteStartElement("graph")
    writer.WriteAttributeString("id", "G")
    writer.WriteAttributeString("edgedefault", "directed")
    users |> Seq.iter(fun u -> writer.WriteStartElement("node"); writer.WriteAttributeString("id", u.UserId.ToString()); writer.WriteEndElement())
    writer.WriteEndElement()
    writer.WriteEndElement()
    writer.Flush()
    writer.Close()

let importStackData () = 
    use inFile = new FileStream(Path.Combine(folder, "posts.xml"), FileMode.Open, FileAccess.Read)
    use reader = XmlReader.Create(inFile)
    reader.MoveToElement() |> ignore
    reader.ReadStartElement("posts")
    let z: Question list * Answer list = (List.empty, List.Empty)
    let (qs, anss) = logIteration(readAllRows(reader), "posts") |> Seq.fold(fun accum elem -> readRow elem accum) z 
    let la = List.length(anss)
    let lq = List.length(anss)
    printfn "Read %d questions and %d answers" lq la
    0

[<EntryPoint>]
let main argv = 
    printfn "Started import at %A." System.DateTime.Now
    printfn "Reading posts.xml"
    importStackData()  |> ignore
    printfn "Writing GraphML"
    writeGraphML(seq { for uid in 1..10 -> { UserId = uid } })
    printfn "Finished import at %A." System.DateTime.Now
    Console.ReadLine() |> ignore
    0
