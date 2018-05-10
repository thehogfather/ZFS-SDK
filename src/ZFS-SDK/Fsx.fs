(* This module contains functions intended to run and generate .fsx scripts *)
module Fsx

open System
open System.IO
open System.Diagnostics
open System.Reflection
open Consensus
open Consensus.Types
open Infrastructure
open Utils
open FsBech32

let generate fileName =
    let code = File.ReadAllText fileName

    let moduleName = ASTUtils.parse_file fileName |> ASTUtils.get_module_name

    let assemblyPath = "output" / moduleName + ".dll"

    let tpl = sprintf """open Zebra
open Consensus
open Types
open Zen.Types.Data
open Zen.Data

module Cost = Zen.Cost.Realized

// Contract Arguments
let contractId = ContractId (Version0, Hash.compute "%s"B)

// Contract Entrypoint
let buyTokenWithZen contractFn costFn =
    let command = ""
    let sender = Anonymous
    let wallet : list<PointedOutput> = []
    let context = {blockNumber=1ul;timestamp=0UL}

    // Data with return address
    let returnAddress = PK Hash.zero |> ZFStar.fsToFstLock |> Lock

    let data =
        Zen.Dictionary.add "returnAddress"B returnAddress Zen.Dictionary.empty
        |> Cost.__force
        |> DataDict
        |> Dict
        |> Some

    // Transaction with one input
    let tx : TxSkeleton.T =
        {
            pInputs=
                [
                    TxSkeleton.PointedOutput ({txHash=Hash.zero;index=0ul},{lock=PK Hash.zero;spend={asset=Asset.Zen;amount=1UL}})
                ];
            outputs=
                [

                ]
        }

    match contractFn tx context contractId command sender data wallet with
    | Ok (tx, message) ->
        printfn "main fn result:\n tx: %%A\n message: %%A" tx message
    | Error error ->
        printfn "main fn error: %%A" error

load "%s"
==> buyTokenWithZen
"""


    let tpl = tpl moduleName assemblyPath

    let fsxFile = changeExtension ".fsx" fileName
    File.WriteAllText (fsxFile, tpl)
    printfn "Generated. to run:\n/ZFS_SDK.exe -r %s" fsxFile
    Ok ""

let run (fsxFile : string) =

    let fsinteractive() : string =
        match Platform.platform with
        | PlatformID.Win32NT -> "fsi"
        | PlatformID.MacOSX | PlatformID.Unix -> "fsharpi"
        | _ -> Environment.OSVersion.Platform.ToString()
               |> failwithf "%s Operating System is Not Supported."

    let workDir = System.Reflection.Assembly.GetExecutingAssembly().Location
                  |> Path.GetDirectoryName

    let args : string =
        String.concat " "
                      [| sprintf "--lib:%s" workDir
                         sprintf "--reference:%s" (workDir/"Zulib.dll")
                         sprintf "--reference:%s" (workDir/"Consensus.dll")
                         sprintf "--reference:%s" (workDir/"Infrastructure.dll")
                         sprintf "--load:%s" (workDir/"ZebraLib.fsx")
                         fsxFile |]
    let pStartInfo = ProcessStartInfo(
                        fsinteractive(),
                        args,
                        RedirectStandardOutput=true,
                        RedirectStandardError=true,
                        UseShellExecute=false
                        )
    use p = new Process(StartInfo=pStartInfo)
    try
        if p.Start() then
            p.WaitForExit()
            printfn "%s" <| p.StandardOutput.ReadToEnd()
            if p.ExitCode = 0
            then Ok ""
            else Error (sprintf "%s" <| p.StandardError.ReadToEnd())
        else
            Error "failed to start fsx"
    with _ as ex ->
        Error (sprintf "failed to run fsx: \n%s" <| ex.ToString())