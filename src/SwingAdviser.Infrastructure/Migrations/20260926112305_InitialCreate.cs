using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SwingAdviser.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_evaluations",
                columns: table => new
                {
                    ai_evaluation_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    stock_code = table.Column<string>(type: "TEXT", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    started_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    verdict = table.Column<string>(type: "TEXT", nullable: true),
                    confidence = table.Column<string>(type: "TEXT", nullable: true),
                    summary = table.Column<string>(type: "TEXT", nullable: true),
                    error_message = table.Column<string>(type: "TEXT", nullable: true),
                    invalidation_conditions = table.Column<string>(type: "TEXT", nullable: false),
                    positive_factors = table.Column<string>(type: "TEXT", nullable: false),
                    risk_factors = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_evaluations", x => x.ai_evaluation_id);
                });

            migrationBuilder.CreateTable(
                name: "candidate_evaluations",
                columns: table => new
                {
                    candidate_evaluation_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    evaluation_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    stock_code = table.Column<string>(type: "TEXT", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    score = table.Column<int>(type: "INTEGER", nullable: false),
                    confidence = table.Column<string>(type: "TEXT", nullable: false),
                    close = table.Column<string>(type: "TEXT", nullable: false),
                    macd_line = table.Column<string>(type: "TEXT", nullable: false),
                    macd_signal = table.Column<string>(type: "TEXT", nullable: false),
                    macd_histogram = table.Column<string>(type: "TEXT", nullable: false),
                    previous_macd_histogram = table.Column<string>(type: "TEXT", nullable: false),
                    ema20 = table.Column<string>(type: "TEXT", nullable: false),
                    ema100 = table.Column<string>(type: "TEXT", nullable: false),
                    ema100_twenty_days_ago = table.Column<string>(type: "TEXT", nullable: false),
                    atr14 = table.Column<string>(type: "TEXT", nullable: false),
                    volume_ratio = table.Column<string>(type: "TEXT", nullable: false),
                    macd_cross_age_days = table.Column<int>(type: "INTEGER", nullable: false),
                    market_regime_aligned = table.Column<bool>(type: "INTEGER", nullable: false),
                    macd_freshness_score = table.Column<int>(type: "INTEGER", nullable: false),
                    macd_position_score = table.Column<int>(type: "INTEGER", nullable: false),
                    macd_momentum_score = table.Column<int>(type: "INTEGER", nullable: false),
                    trend_strength_score = table.Column<int>(type: "INTEGER", nullable: false),
                    volume_score = table.Column<int>(type: "INTEGER", nullable: false),
                    market_regime_score = table.Column<int>(type: "INTEGER", nullable: false),
                    strategy_parameters_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_candidate_evaluations", x => x.candidate_evaluation_id);
                });

            migrationBuilder.CreateTable(
                name: "daily_bars",
                columns: table => new
                {
                    daily_bar_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    stock_code = table.Column<string>(type: "TEXT", nullable: false),
                    trade_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    open = table.Column<string>(type: "TEXT", nullable: false),
                    high = table.Column<string>(type: "TEXT", nullable: false),
                    low = table.Column<string>(type: "TEXT", nullable: false),
                    close = table.Column<string>(type: "TEXT", nullable: false),
                    volume = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_bars", x => x.daily_bar_id);
                });

            migrationBuilder.CreateTable(
                name: "holding_evaluations",
                columns: table => new
                {
                    holding_evaluation_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    evaluation_date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    position_id = table.Column<long>(type: "INTEGER", nullable: false),
                    decision = table.Column<string>(type: "TEXT", nullable: false),
                    reason = table.Column<string>(type: "TEXT", nullable: false),
                    close = table.Column<string>(type: "TEXT", nullable: false),
                    atr14 = table.Column<string>(type: "TEXT", nullable: false),
                    macd_line = table.Column<string>(type: "TEXT", nullable: false),
                    macd_signal = table.Column<string>(type: "TEXT", nullable: false),
                    ema20 = table.Column<string>(type: "TEXT", nullable: false),
                    stop_loss_price = table.Column<string>(type: "TEXT", nullable: false),
                    achieved_r_multiple = table.Column<string>(type: "TEXT", nullable: false),
                    holding_business_days = table.Column<int>(type: "INTEGER", nullable: false),
                    strategy_parameters_json = table.Column<string>(type: "TEXT", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holding_evaluations", x => x.holding_evaluation_id);
                });

            migrationBuilder.CreateTable(
                name: "positions",
                columns: table => new
                {
                    position_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    stock_code = table.Column<string>(type: "TEXT", nullable: false),
                    direction = table.Column<string>(type: "TEXT", nullable: false),
                    is_margin = table.Column<bool>(type: "INTEGER", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false),
                    initial_atr = table.Column<string>(type: "TEXT", nullable: false),
                    stop_loss_price = table.Column<string>(type: "TEXT", nullable: false),
                    memo = table.Column<string>(type: "TEXT", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.position_id);
                });

            migrationBuilder.CreateTable(
                name: "stocks",
                columns: table => new
                {
                    stock_code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    market_segment = table.Column<string>(type: "TEXT", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stocks", x => x.stock_code);
                });

            migrationBuilder.CreateTable(
                name: "executions",
                columns: table => new
                {
                    execution_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    side = table.Column<string>(type: "TEXT", nullable: false),
                    executed_at_utc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    price = table.Column<string>(type: "TEXT", nullable: false),
                    quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    split_factor = table.Column<string>(type: "TEXT", nullable: false),
                    margin_due_date = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    position_id = table.Column<long>(type: "INTEGER", nullable: false),
                    correction_log = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_executions", x => x.execution_id);
                    table.ForeignKey(
                        name: "fk_executions_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "positions",
                        principalColumn: "position_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_candidate_evaluations_stock_code_evaluation_date_direction",
                table: "candidate_evaluations",
                columns: new[] { "stock_code", "evaluation_date", "direction" });

            migrationBuilder.CreateIndex(
                name: "ix_daily_bars_stock_code_trade_date",
                table: "daily_bars",
                columns: new[] { "stock_code", "trade_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_executions_position_id",
                table: "executions",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_holding_evaluations_position_id_evaluation_date",
                table: "holding_evaluations",
                columns: new[] { "position_id", "evaluation_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_evaluations");

            migrationBuilder.DropTable(
                name: "candidate_evaluations");

            migrationBuilder.DropTable(
                name: "daily_bars");

            migrationBuilder.DropTable(
                name: "executions");

            migrationBuilder.DropTable(
                name: "holding_evaluations");

            migrationBuilder.DropTable(
                name: "stocks");

            migrationBuilder.DropTable(
                name: "positions");
        }
    }
}
