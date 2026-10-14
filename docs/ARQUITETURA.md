# Arquitetura e manutenção — XML A1 1.3.0

Aplicação Windows Forms em C#, para .NET Framework 4.8. O projeto usa o compilador
do Framework diretamente, sem NuGet, SQL Anywhere ou bibliotecas do Domínio.
O executável inclui a interface, a lógica de consulta e os testes offline.

## Mapa dos fontes

| Arquivo | Responsabilidade |
| --- | --- |
| `src/Program.cs` | Versão, ponto de entrada e opções `--demo` / `--self-test`. |
| `src/MainForm.cs` | Interface principal, carregamento do A1, biblioteca, filtros, importação e coordenação das tarefas. |
| `src/Core.cs` | Validações, certificado, XML, armazenamento, cursor NSU, comunicação SOAP/TLS e assinatura dos eventos. |
| `src/Batch.cs` | Colagem de chaves, datas, persistência da fila, limites de consultas, prioridades e reconsultas. |
| `src/AutoScience.cs` | Autorização, elegibilidade, histórico durável de ciência e agendamento posterior do XML. |
| `src/KeyBatchForm.cs` | Tela de colagem, autorização da ciência na fila, início, pausa, reconsulta e resultados. |
| `src/QueueSpreadsheet.cs` | Exportação XLSX sem dependência do Excel; chaves gravadas como texto. |
| `src/SelfTests.cs` | Dados fictícios, testes básicos e servidor localhost para validar mTLS. Também fornece os dados do modo demo. |
| `src/ScienceTests.cs` | Testes de manifestação, protocolo, rejeição 596, falhas e recuperação. |
| `src/DownloadTests.cs` | Testes do download automático de todas as chaves e prioridade das consultas novas. |

Todos os arquivos `src/*.cs` são necessários ao build atual. Os testes não carregam
certificados do usuário. `test.ps1` cria um certificado sintético temporário e o remove
ao terminar. Nenhum PFX faz parte da distribuição.

## Fluxo de consulta

1. `CertificateSession` lê o PFX/P12 escolhido na interface. Valida a chave privada,
   validade e vínculo de raiz do CNPJ. O campo de senha é limpo após a tentativa.
2. `Downloader.Run` adquire exclusão local por CNPJ/ambiente e verifica as pausas.
3. `SefazClient` transmite o pedido SOAP usando TLS 1.2 e autenticação mútua.
   A validação do certificado do servidor permanece habilitada no uso normal.
4. `DistributionResult.Parse` lê o retorno e descompacta os `docZip`, com limites
   de quantidade e tamanho. DTD e resolução de entidades externas são bloqueadas.
5. `XmlLibrary.Store` preserva os bytes recebidos, classifica e grava atomicamente.
6. O cursor de distribuição só avança depois de salvar todos os documentos do lote.
   Consulta por chave não avança o cursor NSU sequencial.

`ISefaz` separa o transporte da aplicação. Os testes usam implementações simuladas,
sem chamadas aos serviços reais. A exceção à validação de servidor nos testes de TLS
é restrita ao localhost e ao certificado sintético esperado, e é restaurada no fim.

## O que significa baixar

- `nfeProc`: NF-e completa com protocolo. Pode ser classificada como Recebidas,
  Emitidas ou Outras autorizadas, conforme os participantes presentes no XML.
- `resNFe`: resumo. Fica em Resumos e não é contado como XML completo baixado.
- `procEventoNFe` / `resEvento`: eventos, armazenados em Eventos.
- Código 138: documento localizado; o documento pode ser um resumo ou evento.

O programa não reconstrói uma NF-e completa a partir de resumo e não altera o
destinatário para forçar a classificação como Recebidas.

## Fila automática

`KeyQueue.StartDownloads` ativa a espera por XML completo e recupera resultados
antigos de resumo, ausência e espera, preservando baixadas e rejeições definitivas.
Novas consultas têm prioridade sobre reconsultas. O estado é salvo entre as chamadas.

Resumo e código 137 mantêm a chave pendente. A espera inicial é de uma hora;
depois de 24 verificações, passa a seis horas. A fila permanece pendente até obter
o XML, receber rejeição definitiva ou ser pausada pelo usuário.

O limite local de 20 consultas por chave por hora é compartilhado entre tarefas.
Código 656 e falhas ambíguas respeitam a pausa registrada. Os limites locais não
contabilizam chamadas feitas por outros programas na mesma empresa.

## Ciência da Operação

O único evento fiscal implementado para envio é `210210`. A assinatura é produzida
em `SefazClient.ScienceDocument`; o envelope preserva a assinatura após a inclusão
no SOAP. O certificado incluído na assinatura é público, sem a chave privada.

A autorização global é separada da autorização da fila. A segunda se limita às
chaves pendentes daquela fila. Nenhuma opção vem com autorização pré-gravada na
distribuição. O usuário confirma o envio na interface.

Antes de transmitir, o histórico registra uma tentativa incerta. Depois, guarda
pedido, retorno e protocolo. A recuperação de falha ao enfileirar não reenvia o
evento já registrado. Retornos 135, 136 e 573 são conferidos contra chave, ambiente,
tipo de evento e sequência; 136 permanece identificado como sem vínculo.

Rejeição 596 não é tratada como ciência aceita. No modo de download, a consulta
do XML continua sem reenviar o evento. Não existe fallback para o evento 210200
(Confirmação da Operação). Resultado incerto ou erro de certificado pausa novos
eventos; o histórico é preservado para revisão.

## Arquivos em execução

- Biblioteca: pasta escolhida pelo usuário, separada por CNPJ e ambiente.
- Preferências: `%LOCALAPPDATA%\XML-A1\preferencias.xml`.
- Cursor e pausas: `%LOCALAPPDATA%\XML-A1\estado`.
- Fila: `%LOCALAPPDATA%\XML-A1\filas`.
- Autorização e histórico da ciência: `%LOCALAPPDATA%\XML-A1\ciencia`.
- Pedidos e respostas de eventos: `Manifestacoes` na biblioteca da empresa.

Esses dados são privados e não pertencem ao repositório. A preferência lembra o
caminho do PFX, mas não sua senha. A biblioteca pode conter dados pessoais e fiscais.
Não compartilhe logs ou pastas de execução como parte de uma atualização do código.

## Build, testes e distribuição

`build.ps1` compila `src/*.cs` e copia a configuração do Framework e o README.
O parâmetro `-Output` permite escolher uma pasta fora do repositório.
`test.ps1`, executado com PowerShell 7, recompila em pasta temporária e executa
165 verificações offline, incluindo assinatura e autenticação mútua com localhost.

`dist/XmlA1.exe` é uma compilação deste código. `dist/SHA256SUMS.txt` permite
conferir sua integridade; o arquivo não é uma assinatura digital do distribuidor.
O binário não contém certificados, senhas ou dados de empresas pré-carregados.
O executável não é assinado com certificado de assinatura de código.

Antes de publicar uma versão, compile, execute os testes e revise o índice do Git.
Use uma lista explícita de arquivos permitidos. A `.gitignore` deste projeto bloqueia
por padrão arquivos novos que não pertençam à lista de publicação, além de bloquear
extensões de chaves/certificados e pastas de dados.
