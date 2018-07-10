﻿#load @"FLooping.Core.fsx"

open System
open System.Threading
open Microsoft.FSharp.Data.UnitSystems.SI.UnitSymbols
open FLooping.Core


[<AutoOpen>]
module Math =
    let pi = Math.PI
    let pi2 = 2.0 * pi


[<AutoOpen>]
module AudioEnvironment =

    type Env = {
        samplePos: int;
        sampleRate: int;
    }

    // let toSeconds (env:Env) = (env.samplePos / env.sampleRate) * 1.0<s>

    let env() = L(fun p (r:Env) -> {value=r; state=()})


[<AutoOpen>]
module Seq =

    let toSequence (loop:L<_,_,Env>) sampleRate =
        loop
        |> toReaderSequence (fun i -> { samplePos=i; sampleRate=sampleRate })

    let toAudioSequence (l:L<_,_,_>) = toSequence l 44100

    let toList count (l:L<_,_,_>) =
        (toAudioSequence l)
        |> Seq.take count
        |> Seq.toList


[<AutoOpen>]
module Oscillators =

    // TODO
    // static calculation result in strange effects when modulating :D
    //let sin (frq:float<Hz>) (phase:float<Deg>) =
    //    let f (env:Env) = 
    //        let rad = env.samplePos / env.sampleRate
    //        Math.Sin(rad * pi2 * (float frq))
    //    build (lift_s f) ()
        
    let private osc (frq:float) f =
        let f angle (env:Env) =
            let newAngle = (angle + pi2 * frq / (float env.sampleRate)) % pi2
            {value=f newAngle; state=newAngle}
        f |> liftSeed 0.0 |> L

    // TODO: phase
    let sin (frq:float) = osc frq Math.Sin
    let saw (frq:float) = osc frq (fun angle ->
        1.0 - (1.0 / pi * angle))
    let tri (frq:float) = osc frq (fun angle ->
        if angle < pi
        then -1.0 + (2.0 / pi) * angle
        else 3.0 - (2.0 / pi) * angle)
    let square (frq:float) = osc frq (fun angle ->
        if angle < pi then 1.0 else -1.0)


[<AutoOpen>]
module Filters =

    type BiQuadCoeffs = {
        a0: float;
        a1: float;
        a2: float;
        b1: float;
        b2: float;
        z1: float;
        z2: float;
    }

    type BiQuadParams = {
        gainDb: float;
        q: float;
        frq: float;
    }

    let biQuadCoeffsZero = {a0=0.0; a1=0.0; a2=0.0; b1=0.0; b2=0.0; z1=0.0; z2=0.0}

    let biQuadBase input (filterParams:BiQuadParams) (calcCoeffs:Env->BiQuadCoeffs) =
        let f p r =
            // seed: if we are run the first time, use default values for lastParams+lastCoeffs
            let lastParams,lastCoeffs =
                match p with
                | None ->   
                    (
                        filterParams,
                        calcCoeffs r
                    )
                | Some t -> t
            
            // calc the coeffs new if filter params have changed
            let coeffs =
                match lastParams = filterParams with
                | true -> lastCoeffs
                | false -> calcCoeffs r
            
            let o = input * coeffs.a0 + coeffs.z1
            let z1 = input * coeffs.a1 + coeffs.z2 - coeffs.b1 * o
            let z2 = input * coeffs.a2 - coeffs.b2 * o
            
            let newCoeffs = { coeffs with z1=z1; z2=z2 }

            {value=o; state=(filterParams,newCoeffs)}
        L f

    let lp input (p:BiQuadParams) =
        let calcCoeffs (env:Env) =
            let k = Math.Tan(pi * p.frq / float env.sampleRate)
            let norm = 1.0 / (1.0 + k / p.q + k * k)
            let a0 = k * k * norm
            let a1 = 2.0 * a0
            let a2 = a0
            let b1 = 2.0 * (k * k - 1.0) * norm
            let b2 = (1.0 - k / p.q + k * k) * norm
            { biQuadCoeffsZero with a0=a0; a1=a1; a2=a2; b1=b1; b2=b2 }
        biQuadBase input p calcCoeffs
