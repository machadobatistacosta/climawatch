# ClimaWatch - Infraestrutura Local

Este diretório contém a definição da infraestrutura local em Docker Compose para o projeto **ClimaWatch**.

A stack é composta por:
* **PostgreSQL (v18-alpine)**: Banco de dados relacional.
* **RabbitMQ (v4-management)**: Broker de mensagens.
* **pgAdmin (v4)**: Interface web para gerenciar o PostgreSQL.
* **ClimaWatch.Api (.NET 10)**: API ASP.NET Core mínima.

---

## Como Executar a Stack (Windows PowerShell)

Siga os comandos abaixo no PowerShell para configurar e iniciar os containers:

1. **Criar arquivo de variáveis de ambiente a partir do exemplo**:
   ```powershell
   Copy-Item .env.example .env
   ```

2. **Validar a configuração do Docker Compose**:
   ```powershell
   docker compose config
   ```

3. **Subir os serviços em segundo plano (detached)**:
   ```powershell
   docker compose up -d
   ```

4. **Verificar o status dos containers**:
   ```powershell
   docker compose ps
   ```

5. **Acompanhar logs dos serviços**:
   ```powershell
   docker compose logs -f
   ```

6. **Parar e remover os containers**:
   ```powershell
   docker compose down
   ```

---

## Interfaces Locais e Portas

* **RabbitMQ Management UI**: [http://localhost:15672](http://localhost:15672)
* **pgAdmin Web Interface**: [http://localhost:5050](http://localhost:5050)
* **PostgreSQL Database**: `localhost:5432`
* **ClimaWatch API**: [http://localhost:8080](http://localhost:8080)
* **ClimaWatch Health Check**: [http://localhost:8080/health](http://localhost:8080/health)

---

## Configuração do PostgreSQL no pgAdmin

Ao adicionar um novo servidor PostgreSQL na interface web do pgAdmin (`http://localhost:5050`):

1. Vá na aba **Connection**.
2. No campo **Host name/address**, informe exatamente:
   ```txt
   postgres
   ```
   > [!IMPORTANT]  
   > Use `postgres` e **não** `localhost`. Como o pgAdmin está rodando dentro da mesma rede Docker (`climawatch-network`), o Host name é resolvido pelo nome do serviço definido no `compose.yaml`.

3. Use as credenciais definidas no arquivo `.env` (exemplo: usuário `climawatch`, senha `climawatch_dev_password`, banco `climawatch`).

---

## Dead Letter Queue (DLQ)

Para capturar e isolar mensagens que falham definitivamente no processamento (ex.: JSON inválido, contratos inconsistentes), a topologia local utiliza uma DLQ (Dead Letter Queue):

* **Exchange DLX**: `climawatch.dead-letter` (tipo `direct`)
* **Fila DLQ**: `climawatch.dead-letter` (vinculada à key `dead-letter`)

As filas principais (`climawatch.weather-checks` e `climawatch.alerts`) são configuradas com:
* `x-dead-letter-exchange`: `climawatch.dead-letter`
* `x-dead-letter-routing-key`: `dead-letter`

Quando um worker rejeita uma mensagem enviando `NACK (requeue: false)` por erro irrecuperável, o RabbitMQ redireciona automaticamente a mensagem para a DLQ.

### Testando a DLQ Localmente

Você pode forçar o envio de mensagens para a DLQ publicando payloads inválidos via RabbitMQ HTTP API (Management Plugin):

1. **Testar DLQ com Fila de Weather Checks (`weather.check.requested`)**:
   ```powershell
   $creds = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("climawatch:climawatch_dev_password"))
   $body = @{
       properties  = @{ delivery_mode = 2 }
       routing_key = "weather.check.requested"
       payload     = "invalid json weather checks"
       payload_encoding = "string"
   } | ConvertTo-Json

   Invoke-RestMethod -Method Post `
     -Uri "http://localhost:15672/api/exchanges/%2F/climawatch.events/publish" `
     -Headers @{ Authorization = "Basic $creds"; "Content-Type" = "application/json" } `
     -Body $body
   ```

2. **Testar DLQ com Fila de Alertas (`alert.detected`)**:
   ```powershell
   $creds = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("climawatch:climawatch_dev_password"))
   $body = @{
       properties  = @{ delivery_mode = 2 }
       routing_key = "alert.detected"
       payload     = "invalid json alerts"
       payload_encoding = "string"
   } | ConvertTo-Json

   Invoke-RestMethod -Method Post `
     -Uri "http://localhost:15672/api/exchanges/%2F/climawatch.events/publish" `
     -Headers @{ Authorization = "Basic $creds"; "Content-Type" = "application/json" } `
     -Body $body
   ```

3. **Verificar Filas**:
   Execute o comando abaixo para verificar se as mensagens foram movidas para a DLQ:
   ```powershell
   docker compose exec rabbitmq rabbitmqctl list_queues name messages_ready consumers
   ```
   Resultado esperado após rodar ambos os testes:
   ```txt
   climawatch.dead-letter      2   0
   climawatch.weather-checks   0   1
   climawatch.alerts           0   1
   ```

---

## Notificações via Telegram (Opcional)

O **NotificationWorker** pode enviar alertas reais para o Telegram. Para habilitar essa funcionalidade:

1. Crie um Bot no Telegram usando o [@BotFather](https://t.me/BotFather) e copie o Token gerado.
2. Descubra o Chat ID do chat ou canal para onde deseja enviar os alertas (ex: usando bots como [@userinfobot](https://t.me/userinfobot) ou chamando a API de `getUpdates`).
3. Adicione essas informações no seu arquivo `.env`:
   ```properties
   TELEGRAM_BOT_TOKEN=seu_token_aqui
   TELEGRAM_CHAT_ID=seu_chat_id_aqui
   ```
4. Se essas variáveis estiverem vazias ou ausentes, o worker apenas salvará as notificações no banco local com canal `database` e status `created`, sem tentar contatar o Telegram.
