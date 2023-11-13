parser grammar MoiraiParser;
@header {
    namespace Moirai.Parser;
}
options {

  tokenVocab=moirai_lexer;

}
r: COMMENT* (comment|action|event|prop_definition|enum_definition|type_definition|tag_definition|LINE_BREAK)+ EOF;
comment: COMMENT LINE_BREAK;
filter:AT (occurence=NUMBER ID years=NUMBER)? ID LINE_BREAK?;
action: filter? RULE  ID categories SCOPE_OPEN LINE_BREAK effect+ SCOPE_CLOSE LINE_BREAK*;
categories: ID* ;
event: EVENT ID categories SCOPE_OPEN LINE_BREAK when_tag+ when* effect+ SCOPE_CLOSE LINE_BREAK*;
when: WHEN (VAR_ID COLON)? expr (COMMA expr)* SPACE* LINE_BREAK+;
when_tag: WHEN TAG_ID  SPACE* LINE_BREAK+;
effect: (comment|set | var | call_assign|if|match) SPACE* (comment|LINE_BREAK)*;
if: IF cond=expr then=scope (ELSE LINE_BREAK*  else=scope)? ;
match: (MATCH|MATCH_WEIGHT) expr (COMMA expr)* SCOPE_OPEN LINE_BREAK* match_case+ SCOPE_CLOSE  LINE_BREAK*;
match_case: value (COMMA value)* ARROW ((effect LINE_BREAK+)|scope) ;
set: SET  path EQ expr;
var: VAR  VAR_ID (COLON (ID|TYPE_ID))? EQ expr;
call_assign : ID (VAR_ID COLON)?  ((expr (COMMA expr)* )) scope?;
call : ID ((expr (COMMA expr)* )) scope?;
scope: SCOPE_OPEN LINE_BREAK* (effect|comment)* SCOPE_CLOSE LINE_BREAK*;
value: call | string | enum_value | TYPE_ID | path | bool | number | NULL;
expr
    : left=expr op=(EQ | NEQ | GE | LE | GT | LT) right=expr
    | left=expr op=(MUL | DIV) right=expr
    | left=expr op=(ADD | SUB) right=expr
    | (PAREN_OPEN paren_expr=expr PAREN_CLOSE)
    | value
    | TAG_ID
    ;

tag_definition: TAG TAG_ID LINE_BREAK+ ;

type_definition: ENTITY TYPE_ID SCOPE_OPEN LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;

prop_definition: PROP ID COLON (ID|TYPE_ID) LINE_BREAK+ ;

enum_definition: ENUM TYPE_ID SCOPE_OPEN LINE_BREAK* TYPE_ID (COMMA LINE_BREAK* TYPE_ID)* COMMA? LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;

string: STRING ;

bool: TRUE | FALSE;
path : (SINGLETON_ID | VAR_ID) (DOT ID)* | ID;
enum_value: TYPE_ID DOT TYPE_ID ;
number: NUMBER;
