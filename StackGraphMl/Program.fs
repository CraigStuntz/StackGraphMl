open System
open System.Data
open System.Data.Linq
open System.IO
open System.Linq
open System.Xml
open Microsoft.FSharp.Data.TypeProviders
open Microsoft.FSharp.Linq

let minQuestionVotes = 100 // Smaller min = more questions in result = slower
let folder = @"E:\StackExchangeDump";

type Answer = { Id: int; UserId: int; Score: int; ParentId: int }
type Question = { Id: int; UserId: int; Score: int; Answers: Answer list }

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

let readAllRows (reader : XmlReader) = 
    let i = ref 0
    seq { 
        while reader.IsStartElement("row") do
            reader.ReadStartElement("row")
            i := !i + 1
            if (!i % 100000 = 0) then 
                printf "."
            if (!i % 1000000 = 0) then 
                printfn "Read %d posts" !i
            yield reader 
    }

let importStackData () = 
    use file = new FileStream(Path.Combine(folder, "posts.xml"), FileMode.Open, FileAccess.Read)
    use reader = XmlReader.Create(file)
    reader.MoveToElement() |> ignore
    reader.ReadStartElement("posts")
    let z: Question list * Answer list = (List.empty, List.Empty)
    let (qs, anss) = readAllRows(reader) |> Seq.fold(fun accum elem -> readRow elem accum) z 
    let la = List.length(anss)
    let lq = List.length(anss)
    printfn "Read %d questions and %d answers" lq la
    0

[<EntryPoint>]
let main argv = 
    printfn "Started import at %A." System.DateTime.Now
    importStackData()  |> ignore
    printfn "Started import at %A." System.DateTime.Now
    Console.ReadLine() |> ignore
    0
