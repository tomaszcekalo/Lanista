# Lanista

**A lights-out model factory for ML.NET.** Upload a dataset, pick a task, and let Lanista train and compare models in the background, from any browser, on any OS.

> **Status: early planning.** There is no runnable code yet. This README describes the intended scope; everything below is subject to change.

## Why

The [ML.NET CLI](https://learn.microsoft.com/dotnet/machine-learning/automate-training-with-cli) (`mlnet`) can train classification, regression and other models with AutoML, but the only graphical front end for it is Model Builder, which requires Visual Studio on Windows. That leaves out Linux users, containers, CI boxes and anyone who prefers a browser.

Lanista fills that gap: a web UI that runs the real `mlnet` CLI as a background process and shows you what it is doing.

A *lanista* trained gladiators and sent them into the arena. Lanista does the same with models: it trains many candidates, pits them against each other, and hands you the winner.

## Planned features

- **Cross-platform.** Runs on Windows, Linux and macOS, with no Visual Studio, and ships as a container image.
- **Real `mlnet` under the hood.** Training jobs are `mlnet classification`, `regression` and other scenarios launched as background processes, not a reimplementation.
- **Dataset upload.** Upload a CSV or TSV, choose the label column and the scenario, and set a training time budget.
- **Live progress.** Follow the trainer log of a running job and see candidate models appear as they are evaluated.
- **Model comparison.** Compare trainers and metrics side by side and see which model won.
- **Downloads.** Get the trained model (`.zip`) and the generated consumption and training code.
- **Job queue.** Run several training jobs and keep their history.

## Non-goals

- Not a replacement for the ML.NET CLI or Model Builder; Lanista wraps the CLI.
- Not an LLM benchmarking tool.
- Not a hosted service. It is meant to be self-hosted.

## Requirements (planned)

- [.NET SDK](https://dotnet.microsoft.com/download), to run Lanista and to install the ML.NET CLI
- The ML.NET CLI for your platform (installed as a .NET tool)
- Or just Docker, once an image is published

## Getting started

Not available yet. Installation and usage instructions will land here with the first release.

## Roadmap

- [ ] Run a single `mlnet` job from the web UI and stream its output
- [ ] Dataset upload and scenario configuration
- [ ] Job queue and persisted history
- [ ] Model comparison view
- [ ] Download of model and generated code
- [ ] Docker image

## Contributing

Ideas and issues are welcome while the design is still taking shape. Please open an issue before sending a pull request.

## Trademark note

ML.NET and .NET are trademarks of Microsoft. Lanista is an independent project and is not affiliated with or endorsed by Microsoft.

## License

[MIT](LICENSE) © 2026 Tomasz Cekało
