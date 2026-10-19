# Agente Zippy de impressão

Serviço Windows .NET 10 com bandeja, pareamento por código, impressão RAW/TCP e atualização automática. Não precisa de runtime instalado no cliente. O servidor é embutido no build, não configurado pela loja.

Gere o instalador com `powershell -ExecutionPolicy Bypass -File docs/build.ps1 -BackendPath ../motoboyBackEnd`. Isso apenas compila e copia o pacote para a pasta `PrintAgent` do backend; não instala serviço nem publica nada. O backend serve o arquivo e seu SHA-256. Configure `Printing:AgentVersion` na implantação junto do pacote. Versione também os projetos e o heartbeat do agente ao lançar uma nova versão.

O instalador não tem assinatura de editor, por decisão do dono: o Windows pode exibir o SmartScreen ("Mais informações → Executar assim mesmo"). Não há certificado comercial. Instale com administrador. Impressoras do Windows precisam estar instaladas para todos os usuários, acessíveis à conta do serviço. Rede usa IP:porta (9100). A configuração fica toda no painel.

O token usa DPAPI LocalMachine em diretório com ACL somente SYSTEM/Administradores. O ícone da bandeja lê somente estado; pareamento solicita elevação. Diário durável é gravado antes do hardware. Reinício durante envio gera resultado incerto, nunca reimpressão automática. A atualização HTTPS verifica SHA-256 e só é iniciada fora do envio de trabalhos. RAW/TCP confirmam envio, nunca saída física do papel.

Materiais de execução e validação: `docs/`. Sem GitHub, commit ou push nesta execução.
