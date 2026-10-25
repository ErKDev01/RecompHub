# Como publicar o Recomp Hub para os amigos

Roteiro para quando você decidir pôr o hub no GitHub. Nada disso foi feito ainda.

## Uma vez só: criar o repositório

1. Crie uma conta em [github.com](https://github.com) (grátis), se ainda não tiver.
2. Clique em **New repository**, com o nome **RecompHub**, **Public**, e crie.
   - **Com o código:** envie a pasta inteira (`src`, `fonts`, `tools`, `docs`, `README.md`, `build.ps1`, `icon.ico`).
   - **Só para download:** envie só `README.md` e a pasta `docs` (as imagens do README). O `.exe` vai nas Releases.
3. Abra `src\HubUpdate.cs` e preencha o repositório na linha:
   ```csharp
   public const string Repo = "SEU-USUARIO/RecompHub";
   ```
   **Faça isso antes da primeira release.** Uma cópia publicada com essa linha vazia nunca procura versões novas,
   e os amigos que baixarem essa cópia vão ter de baixar de novo à mão.

## A cada versão

1. Feche o hub e rode, trocando o número (cada versão maior que a anterior: 3.1.0, 3.1.1, 3.2.0…):
   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\release.ps1 -Version 3.1.0
   ```
   O script grava a versão no hub, gera `dist\RecompHub.exe` e mostra o hash.
2. No GitHub, abra o repositório, clique em **Releases → Draft a new release** e preencha:
   - **Tag:** `v3.1.0` (com o "v", igual à versão do passo 1).
   - **Título:** `Recomp Hub 3.1.0`.
   - **Descrição:** o que mudou, em poucas linhas.
   - **Arquivo:** arraste o `dist\RecompHub.exe` (o nome tem de terminar em `.exe`).
3. Clique em **Publish release**.

Os hubs dos seus amigos mostram **"Nova versão do hub"** na barra lateral na próxima vez que forem abertos.
O botão **Atualizar** baixa, confere o hash e reinicia o hub já na versão nova.

## O link para mandar

```
https://github.com/SEU-USUARIO/RecompHub/releases/latest
```

Esse link sempre aponta para a versão mais recente.

## Bom saber

- **Aviso do Windows (SmartScreen):** aparece porque o `.exe` não tem assinatura digital. O README explica
  como passar ("Mais informações → Executar assim mesmo"). Para tirar o aviso de vez, seria preciso um
  certificado de assinatura de código, que é pago.
- **Atualização automática:** só funciona se a pasta onde o amigo guardou o `RecompHub.exe` permitir escrita.
  Em `Arquivos de Programas` não permite; aí o hub abre a página de download.
