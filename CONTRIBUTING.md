# Como contribuir

Obrigado por ajudar a construir observabilidade segura para ambientes Protheus.

## Preparação

1. instale SDK .NET 8 e Node.js compatível com Vite 8;
2. execute `npm ci` e `dotnet restore`;
3. use somente fixtures sintéticas;
4. crie uma branch curta e uma alteração focada.

## Qualidade obrigatória

```powershell
dotnet build ProtheusPulse.sln --configuration Release
dotnet test ProtheusPulse.sln --configuration Release --no-build
npm run ui:test
npm run ui:build
npm run ui:e2e
npm audit --audit-level=moderate
```

Novos coletores devem implementar contrato simulável, usar operações assíncronas, `CancellationToken`, timeout e limites. Um resultado incerto deve ser `Unknown`, com motivo sanitizado.

## Privacidade

Nunca faça commit de logs, INIs, IPs, nomes, caminhos, certificados, tokens ou screenshots de clientes. Exemplos devem usar nomes e dados evidentemente fictícios.

## Pull request

Descreva problema, decisão, risco, testes e validação manual. Atualize documentação e `CHANGELOG.md` quando houver mudança observável.
