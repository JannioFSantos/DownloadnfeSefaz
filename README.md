# Download NF-e SEFAZ — XML A1

Aplicativo Windows independente para consultar NF-e modelo 55 com certificado e-CNPJ
A1 e organizar uma biblioteca local de XMLs. Versão **1.3.0**, C# / .NET Framework 4.8.

## Programa compilado

Baixe o repositório em **Code → Download ZIP**, extraia e abra **dist/XmlA1.exe**,
mantendo **dist/XmlA1.exe.config** na mesma pasta. Também é possível clonar:

~~~powershell
git clone https://github.com/JannioFSantos/DownloadnfeSefaz.git
cd DownloadnfeSefaz
.\dist\XmlA1.exe
~~~

Requer Windows com **.NET Framework 4.8**. Não depende do Domínio, SQL Anywhere,
Excel ou NuGet. O executável não possui expiração própria. O certificado e as
permissões dos serviços precisam estar válidos. O binário não possui assinatura
de código do distribuidor.

Confira a integridade comparando os hashes:

~~~powershell
Get-FileHash .\dist\XmlA1.exe, .\dist\XmlA1.exe.config -Algorithm SHA256
Get-Content .\dist\SHA256SUMS.txt
~~~

## Primeira consulta

1. Escolha o A1 .pfx/.p12, informe a senha no aplicativo e clique em **Carregar A1**.
   A senha não é salva e o campo é limpo após a tentativa.
2. Confira CNPJ, UF e ambiente. Use **Produção** para documentos reais.
3. Escolha a pasta dos XMLs.
4. Clique em **Buscar novas NF-e**. Se já usa outro buscador para a empresa,
   informe o último NSU dele na primeira execução; depois o cursor é mantido.

Evite consultas concorrentes de sistemas com sequências diferentes. Não apague os
arquivos de estado para reiniciar NSUs indiscriminadamente.

## Baixar chaves de planilha

1. Carregue o A1 e abra **Consultar chaves**.
2. Cole as células com **Ctrl+V**: várias linhas e colunas, CSV simples, espaços
   entre grupos e prefixo NFe são reconhecidos.
3. Clique em **Validar e adicionar**. Confira válidas, inválidas e repetidas.
4. Clique em **Baixar / continuar XMLs**.

As opções de download automático e ciência quando necessária aparecem marcadas;
o envio de ciência exige autorização na interface. A autorização da fila se limita
às suas chaves pendentes e é separada da ciência global da tela principal.

O programa consulta primeiro e salva o XML completo sem enviar evento. Quando
recebe só um resumo elegível e a opção está autorizada, pode enviar **Ciência da
Operação (210210)** e aguardar a disponibilização. Resumo, evento e ausência de
documento mantêm a chave pendente: a espera inicial é de uma hora e passa a seis
horas após 24 verificações. Consultas novas têm prioridade sobre reconsultas.

A fila permanece salva ao fechar. Para retomar, abra o aplicativo e carregue o A1,
mantendo empresa e ambiente correspondentes. Não há serviço em segundo plano.
**Parar** interrompe a tarefa; **Pausar automática** suspende a retomada da fila.
**Baixar / continuar XMLs** também recupera resultados antigos de resumo, ausência
e espera, preservando baixadas e rejeições definitivas. **Recolocar selecionadas**
solicita nova tentativa, mantendo os limites.

É possível exportar resultados em XLSX; as chaves ficam como texto, com 44 posições.
Se a planilha perdeu dígitos por notação científica, recupere os valores na origem:
mudar o formato depois não recupera os dígitos perdidos.

## Documentos e limites

| Guia / retorno | Significado |
| --- | --- |
| **Recebidas** | NF-e completa em que o CNPJ consultado é destinatário. |
| **Emitidas** | XMLs importados do emissor. O serviço nacional não disponibiliza ao emitente o XML completo das próprias emissões. |
| **Resumos** | resNFe: informações resumidas, sem o XML completo da NF-e. |
| **Eventos** | Eventos distribuídos, como cancelamento e manifestações. |
| **Outras autorizadas** | NF-e completa disponibilizada a um terceiro autorizado. |
| **138** | Documento localizado: pode ser XML completo, resumo ou evento. |
| **596** | Evento rejeitado por prazo; não comprova ciência anterior. A consulta de XML continua no modo de download. |
| **656** | Consumo indevido: o programa respeita a pausa registrada. |

Somente nfeProc com conteúdo e protocolo é tratado como NF-e completa. O programa
não reconstrói notas a partir de resumos nem muda o destinatário para classificá-las
como recebidas. A disponibilização depende da SEFAZ, da permissão, do histórico e
da manifestação aplicável. Passar de dez dias não libera automaticamente o XML.

O programa **não envia Confirmação da Operação (210200)** para substituir ciência
rejeitada. Ciência registra conhecimento; a manifestação conclusiva aplicável
deve ser decidida pelo usuário. Tentativas incertas ficam registradas e não são
repetidas automaticamente. Pedidos e retornos ficam em Manifestacoes na biblioteca.

Há limite local de **20 consultas por chave por hora**. Distribuição sequencial
respeita as pausas de 137, maxNSU e 656. Falhas ambíguas de rede também suspendem
repetições. Esses controles não contabilizam consultas de outros programas.

O filtro **De / até** seleciona o histórico já disponível, usando a data de emissão;
eventos usam a data do evento. Não cria uma consulta por datas no serviço nacional.
**Exportar XMLs** gera um ZIP da guia e dos filtros atuais.

## Certificados e dados pessoais

**Nenhum certificado digital, chave privada, senha, XML fiscal real ou configuração
de empresa é distribuído neste repositório.** Os exemplos dos testes são sintéticos.

O A1 é escolhido localmente pelo usuário. O aplicativo não salva a senha nem cria
cópia do PFX. Usa um contêiner temporário de chave do Windows para TLS, sem instalar
o certificado no repositório pessoal; ele é liberado ao descartar a sessão normalmente.
A preferência pode lembrar o caminho do arquivo.

Preferências, NSU, fila e histórico de ciência ficam em %LOCALAPPDATA%\XML-A1.
XMLs e retornos ficam na pasta escolhida pelo usuário. Esses arquivos podem conter
dados pessoais e fiscais e devem permanecer fora do Git.

A .gitignore permite somente os caminhos de publicação definidos e bloqueia
certificados, chaves, dados de execução e material de terceiros. Revise o índice
do Git antes de cada envio; a exclusão não substitui essa revisão.

## Código, compilação e testes

Consulte a [documentação técnica dos módulos e fluxos](docs/ARQUITETURA.md).

~~~powershell
# Windows PowerShell ou PowerShell 7: saída padrão em dist.
.\build.ps1

# Escolher outra saída:
.\build.ps1 -Output C:\Temp\XML-A1-build

# PowerShell 7: compila e executa a suíte completa sem SEFAZ.
pwsh -File .\test.ps1
~~~

O build usa o compilador do .NET Framework em
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe. Não há dependências NuGet.
Se o Windows impedir gravação na pasta do repositório, escolha outra saída com
-Output; não é necessário alterar as proteções do sistema.

São **165 verificações offline**, incluindo XML, NSU, fila, limites, retomada,
ciência, rejeições, datas, colagem, XLSX, assinatura e mTLS com localhost.
test.ps1 cria um certificado sintético temporário e o remove no fim.
Os testes não consultam a SEFAZ nem enviam eventos fiscais reais.
O relatório fica em %USERPROFILE%\BuscaNFe-A1\testes.txt, fora do repositório.

~~~powershell
.\dist\XmlA1.exe --demo       # Interface fictícia, sem rede fiscal.
.\dist\XmlA1.exe --self-test  # Suíte sem certificado; test.ps1 executa a completa.
~~~

O escopo é **NF-e modelo 55**. Não inclui CT-e, NFC-e ou NFS-e. Os fontes e o binário
publicados são desta implementação. Não incluem o programa original do Domínio,
suas DLLs ou fontes descompilados.

## Referências

- [NT 2014.002 v1.40 — distribuição de DF-e](https://www.nfe.fazenda.gov.br/portal/exibirArquivo.aspx?conteudo=uWO2d%2FgTuWg%3D).
- [Serviços web oficiais da NF-e](https://www.nfe.fazenda.gov.br/portal/WebServices.aspx).
- [Receita Federal — cálculo do CNPJ](https://www.gov.br/receitafederal/pt-br/centrais-de-conteudo/publicacoes/documentos-tecnicos/cnpj).
