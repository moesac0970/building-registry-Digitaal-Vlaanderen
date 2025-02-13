#r "paket:
version 7.0.2
framework: net6.0
source https://api.nuget.org/v3/index.json
nuget Be.Vlaanderen.Basisregisters.Build.Pipeline 6.0.5 //"

#load "packages/Be.Vlaanderen.Basisregisters.Build.Pipeline/Content/build-generic.fsx"

open Fake
open Fake.Core
open Fake.Core.TargetOperators
open Fake.IO
open Fake.IO.FileSystemOperators
open ``Build-generic``

let product = "Basisregisters Vlaanderen"
let copyright = "Copyright (c) Vlaamse overheid"
let company = "Vlaamse overheid"

let dockerRepository = "building-registry"
let assemblyVersionNumber = (sprintf "2.%s")
let nugetVersionNumber = (sprintf "%s")

let buildSolution = buildSolution assemblyVersionNumber
let buildSource = build assemblyVersionNumber
let buildTest = buildTest assemblyVersionNumber
let setVersions = (setSolutionVersions assemblyVersionNumber product copyright company)
let test = testSolution
let publishSource = publish assemblyVersionNumber
let pack = pack nugetVersionNumber
let containerize = containerize dockerRepository
let push = push dockerRepository

supportedRuntimeIdentifiers <- [ "msil"; "linux-x64" ]

// Solution -----------------------------------------------------------------------

Target.create "Restore_Solution" (fun _ -> restore "BuildingRegistry")

Target.create "Build_Solution" (fun _ ->
  setVersions "SolutionInfo.cs"
  buildSolution "BuildingRegistry")

Target.create "Test_Solution" (fun _ -> test "BuildingRegistry")

Target.create "Publish_Solution" (fun _ ->
  [
    "BuildingRegistry.Projector"
    "BuildingRegistry.Api.Legacy"
    "BuildingRegistry.Api.Legacy.Abstractions"
    "BuildingRegistry.Api.Legacy.Handlers"
    "BuildingRegistry.Api.Oslo"
    "BuildingRegistry.Api.Oslo.Abstractions"
    "BuildingRegistry.Api.Oslo.Handlers"
    "BuildingRegistry.Api.Extract"
    "BuildingRegistry.Api.Extract.Abstractions"
    "BuildingRegistry.Api.Extract.Handlers"
    "BuildingRegistry.Api.CrabImport"
    "BuildingRegistry.Api.CrabImport.Abstractions"
    "BuildingRegistry.Api.CrabImport.Handlers"
    "BuildingRegistry.Api.BackOffice"
    "BuildingRegistry.Consumer.Address"
    "BuildingRegistry.Projections.Legacy"
    "BuildingRegistry.Projections.Extract"
    "BuildingRegistry.Projections.LastChangedList"
    "BuildingRegistry.Projections.Syndication"
    "BuildingRegistry.Migrator.Building"
  ] |> List.iter publishSource)

Target.create "Pack_Solution" (fun _ ->
  [
    "BuildingRegistry.Projector"
    "BuildingRegistry.Api.Legacy"
    "BuildingRegistry.Api.Legacy.Abstractions"
    "BuildingRegistry.Api.Oslo"
    "BuildingRegistry.Api.Oslo.Abstractions"
    "BuildingRegistry.Api.Extract"
    "BuildingRegistry.Api.Extract.Abstractions"
    "BuildingRegistry.Api.CrabImport"
    "BuildingRegistry.Api.BackOffice"
    "BuildingRegistry.Migrator.Building"
  ] |> List.iter pack)

Target.create "Containerize_Projector" (fun _ -> containerize "BuildingRegistry.Projector" "projector")
Target.create "PushContainer_Projector" (fun _ -> push "projector")

Target.create "Containerize_ApiLegacy" (fun _ -> containerize "BuildingRegistry.Api.Legacy" "api-legacy")
Target.create "PushContainer_ApiLegacy" (fun _ -> push "api-legacy")

Target.create "Containerize_ApiOslo" (fun _ -> containerize "BuildingRegistry.Api.Oslo" "api-oslo")
Target.create "PushContainer_ApiOslo" (fun _ -> push "api-oslo")

Target.create "Containerize_ApiExtract" (fun _ -> containerize "BuildingRegistry.Api.Extract" "api-extract")
Target.create "PushContainer_ApiExtract" (fun _ -> push "api-extract")

Target.create "Containerize_ApiBackOffice" (fun _ -> containerize "BuildingRegistry.Api.BackOffice" "api-backoffice")
Target.create "PushContainer_ApiBackOffice" (fun _ -> push "api-backoffice")

Target.create "Containerize_ApiCrabImport" (fun _ ->
  let dist = (buildDir @@ "BuildingRegistry.Api.CrabImport" @@ "linux")
  let source = "assets" @@ "sss"

  //Shell.copyFile dist (source @@ "SqlStreamStore.dll")
  //Shell.copyFile dist (source @@ "SqlStreamStore.MsSql.dll")

  containerize "BuildingRegistry.Api.CrabImport" "api-crab-import")

Target.create "PushContainer_ApiCrabImport" (fun _ -> push "api-crab-import")

Target.create "Containerize_ProjectionsLegacy" (fun _ -> containerize "BuildingRegistry.Projections.Legacy" "projections-legacy")
Target.create "PushContainer_ProjectionsLegacy" (fun _ -> push "projections-legacy")

Target.create "Containerize_ProjectionsExtract" (fun _ -> containerize "BuildingRegistry.Projections.Extract" "projections-extract")
Target.create "PushContainer_ProjectionsExtract" (fun _ -> push "projections-extract")

Target.create "Containerize_ProjectionsSyndication" (fun _ -> containerize "BuildingRegistry.Projections.Syndication" "projections-syndication")
Target.create "PushContainer_ProjectionsSyndication" (fun _ -> push "projections-syndication")

Target.create "Containerize_ConsumerAddress" (fun _ -> containerize "BuildingRegistry.Consumer.Address" "consumer-address")
Target.create "PushContainer_ConsumerAddress" (fun _ -> push "consumer-address")

Target.create "Containerize_MigratorBuilding" (fun _ -> containerize "BuildingRegistry.Migrator.Building" "migrator-building")
Target.create "PushContainer_MigratorBuilding" (fun _ -> push "migrator-building")

// --------------------------------------------------------------------------------

Target.create "Build" ignore
Target.create "Test" ignore
Target.create "Publish" ignore
Target.create "Pack" ignore
Target.create "Containerize" ignore
Target.create "Push" ignore

"NpmInstall"
  ==> "DotNetCli"
  ==> "Clean"
  ==> "Restore_Solution"
  ==> "Build_Solution"
  ==> "Build"

"Build"
  ==> "Test_Solution"
  ==> "Test"

"Test"
  ==> "Publish_Solution"
  ==> "Publish"

"Publish"
  ==> "Pack_Solution"
  ==> "Pack"

"Pack"
  ==> "Containerize_Projector"
  ==> "Containerize_ApiLegacy"
  ==> "Containerize_ApiOslo"
  ==> "Containerize_ApiExtract"
  ==> "Containerize_ApiCrabImport"
  ==> "Containerize_ApiBackOffice"
  ==> "Containerize_ProjectionsSyndication"
  ==> "Containerize_ConsumerAddress"
  ==> "Containerize_MigratorBuilding"
  ==> "Containerize"  
// Possibly add more projects to containerize here

"Containerize"
  ==> "DockerLogin"
  ==> "PushContainer_Projector"
  ==> "PushContainer_ApiLegacy"
  ==> "PushContainer_ApiOslo"
  ==> "PushContainer_ApiExtract"
  ==> "PushContainer_ApiCrabImport"
  ==> "PushContainer_ApiBackOffice"
  ==> "PushContainer_ProjectionsSyndication"
  ==> "PushContainer_ConsumerAddress"
  ==> "PushContainer_MigratorBuilding"
  ==> "Push"
// Possibly add more projects to push here

// By default we build & test
Target.runOrDefault "Test"
