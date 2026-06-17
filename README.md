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
